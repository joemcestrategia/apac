#include "task_runner.hpp"

#include <algorithm>
#include <cassert>
#include <iostream>

namespace apac {

using namespace std::chrono;

const char* task_status_str(TaskStatus s) {
    switch (s) {
    case TaskStatus::Pending:   return "Pending";
    case TaskStatus::Ready:     return "Ready";
    case TaskStatus::Running:   return "Running";
    case TaskStatus::Completed: return "Completed";
    case TaskStatus::Failed:    return "Failed";
    case TaskStatus::Cancelled: return "Cancelled";
    }
    return "Unknown";
}

void TaskStats::reset() {
    *this = TaskStats{};
}

double TaskStats::completion_rate() const {
    if (total_submitted == 0) return 0.0;
    return static_cast<double>(total_completed) / static_cast<double>(total_submitted);
}

// ─── TaskRunner ────────────────────────────────────────────────────────────

TaskRunner::TaskRunner(size_t thread_count) {
    if (thread_count == 0) {
        thread_count = std::max(1u, std::thread::hardware_concurrency());
    }
    ready_queue_ = std::priority_queue<TaskId, std::vector<TaskId>, PriorityCompare>(
        PriorityCompare{&entries_});

    workers_.reserve(thread_count);
    for (size_t i = 0; i < thread_count; ++i) {
        workers_.emplace_back(&TaskRunner::worker_loop, this);
    }
}

TaskRunner::~TaskRunner() {
    stop();
}

void TaskRunner::stop() {
    stop_flag_ = true;
    cv_work_.notify_all();
    for (auto& w : workers_) {
        if (w.joinable()) w.join();
    }
}

TaskId TaskRunner::submit(TaskFunction func,
                          std::vector<TaskId> dependencies,
                          int priority,
                          std::string name) {
    TaskId id = next_id_++;
    Task task;
    task.id = id;
    task.func = std::move(func);
    task.dependencies = std::move(dependencies);
    task.priority = priority;
    task.name = std::move(name);

    auto entry = std::make_unique<TaskEntry>();
    entry->task = task;
    entry->remaining_deps = task.dependencies.size();
    entry->completion_promise = std::promise<void>{};

    {
        std::lock_guard<std::mutex> lk(mutex_);
        entries_[id] = std::move(entry);

        for (TaskId dep : task.dependencies) {
            auto it = entries_.find(dep);
            if (it != entries_.end()) {
                it->second->dependents.push_back(id);
            }
        }
    }

    TaskStats s = stats_.load();
    s.total_submitted++;
    stats_.store(s);

    if (task.dependencies.empty()) {
        enqueue_ready(id);
    }

    return id;
}

void TaskRunner::submit_task(Task task) {
    std::lock_guard<std::mutex> lk(mutex_);

    TaskId id = task.id;
    auto entry = std::make_unique<TaskEntry>();
    entry->task = std::move(task);
    entry->remaining_deps = entry->task.dependencies.size();
    entry->completion_promise = std::promise<void>{};

    entries_[id] = std::move(entry);

    for (TaskId dep : entries_[id]->task.dependencies) {
        auto it = entries_.find(dep);
        if (it != entries_.end()) {
            it->second->dependents.push_back(id);
        }
    }

    TaskStats s = stats_.load();
    s.total_submitted++;
    stats_.store(s);

    if (entries_[id]->remaining_deps == 0) {
        enqueue_ready(id);
    }
}

void TaskRunner::run_all() {
    {
        std::unique_lock<std::mutex> lk(mutex_);
        cv_idle_.wait(lk, [this] {
            return ready_queue_.empty() && active_tasks_ == 0;
        });
    }
}

void TaskRunner::run_one() {
    TaskId id = -1;
    {
        std::unique_lock<std::mutex> lk(mutex_);
        cv_work_.wait(lk, [this] {
            return !ready_queue_.empty() || stop_flag_;
        });
        if (stop_flag_ || ready_queue_.empty()) return;
        id = ready_queue_.top();
        ready_queue_.pop();
    }
    run_task(id);
    cv_work_.notify_one();
}

void TaskRunner::cancel(TaskId id) {
    std::lock_guard<std::mutex> lk(mutex_);
    auto it = entries_.find(id);
    if (it == entries_.end()) return;

    auto status = it->second->task.status.load();
    if (status == TaskStatus::Pending || status == TaskStatus::Ready) {
        mark_complete(id, TaskStatus::Cancelled);
    }
}

void TaskRunner::cancel_all() {
    std::lock_guard<std::mutex> lk(mutex_);
    for (auto& [id, entry] : entries_) {
        auto status = entry->task.status.load();
        if (status == TaskStatus::Pending || status == TaskStatus::Ready) {
            entry->task.status = TaskStatus::Cancelled;
            try {
                entry->completion_promise.set_exception(
                    std::make_exception_ptr(std::runtime_error("Task cancelled")));
            } catch (...) {}
        }
    }
    // proper cancel instead of draining
    std::priority_queue<TaskId, std::vector<TaskId>, PriorityCompare> empty(
        PriorityCompare{&entries_});
    ready_queue_.swap(empty);

    TaskStats s = stats_.load();
    s.total_cancelled = s.total_submitted - s.total_completed - s.total_failed;
    s.currently_queued = 0;
    stats_.store(s);
}

void TaskRunner::wait_all() {
    std::vector<std::shared_future<void>> futures;
    {
        std::lock_guard<std::mutex> lk(mutex_);
        futures.reserve(entries_.size());
        for (auto& [_, entry] : entries_) {
            futures.push_back(entry->completion_promise.get_future().share());
        }
    }
    for (auto& f : futures) {
        try { f.wait(); } catch (...) {}
    }
}

void TaskRunner::wait_one(TaskId id) {
    std::shared_future<void> f;
    {
        std::lock_guard<std::mutex> lk(mutex_);
        auto it = entries_.find(id);
        if (it == entries_.end()) return;
        f = it->second->completion_promise.get_future().share();
    }
    try { f.wait(); } catch (...) {}
}

TaskStatus TaskRunner::status(TaskId id) const {
    std::lock_guard<std::mutex> lk(mutex_);
    auto it = entries_.find(id);
    if (it == entries_.end()) return TaskStatus::Cancelled;
    return it->second->task.status.load();
}

TaskStats TaskRunner::stats() const {
    return stats_.load();
}

size_t TaskRunner::thread_count() const {
    return workers_.size();
}

size_t TaskRunner::pending_count() const {
    size_t count = 0;
    std::lock_guard<std::mutex> lk(mutex_);
    for (const auto& [_, entry] : entries_) {
        if (entry->task.status == TaskStatus::Pending) ++count;
    }
    return count;
}

size_t TaskRunner::ready_count() const {
    std::lock_guard<std::mutex> lk(mutex_);
    return ready_queue_.size();
}

std::exception_ptr TaskRunner::task_error(TaskId id) const {
    std::lock_guard<std::mutex> lk(mutex_);
    auto it = entries_.find(id);
    if (it == entries_.end()) return nullptr;
    return it->second->task.error;
}

void TaskRunner::rethrow_if_error(TaskId id) const {
    auto err = task_error(id);
    if (err) std::rethrow_exception(err);
}

// ─── private ───────────────────────────────────────────────────────────────

void TaskRunner::worker_loop() {
    while (!stop_flag_) {
        TaskId id = -1;
        {
            std::unique_lock<std::mutex> lk(mutex_);
            cv_work_.wait(lk, [this] {
                return !ready_queue_.empty() || stop_flag_;
            });
            if (stop_flag_) break;
            if (ready_queue_.empty()) continue;

            id = ready_queue_.top();
            ready_queue_.pop();
        }
        run_task(id);
    }
}

void TaskRunner::enqueue_ready(TaskId id) {
    std::lock_guard<std::mutex> lk(mutex_); // already locked by caller
    auto it = entries_.find(id);
    if (it == entries_.end()) return;
    if (it->second->task.status != TaskStatus::Pending) return;

    it->second->task.status = TaskStatus::Ready;
    ready_queue_.push(id);

    TaskStats s = stats_.load();
    s.currently_queued++;
    stats_.store(s);

    cv_work_.notify_one();
}

void TaskRunner::mark_complete(TaskId id, TaskStatus final_status) {
    // caller holds mutex_
    auto it = entries_.find(id);
    if (it == entries_.end()) return;

    auto& entry = *it->second;
    if (entry.task.status == TaskStatus::Completed ||
        entry.task.status == TaskStatus::Failed ||
        entry.task.status == TaskStatus::Cancelled) {
        return;
    }

    entry.task.status = final_status;

    TaskStats s = stats_.load();
    if (final_status == TaskStatus::Completed) s.total_completed++;
    else if (final_status == TaskStatus::Failed) s.total_failed++;
    else if (final_status == TaskStatus::Cancelled) s.total_cancelled++;

    if (s.currently_queued > 0) s.currently_queued--;
    stats_.store(s);

    // notify dependents
    for (TaskId dep_id : entry.dependents) {
        auto dep_it = entries_.find(dep_id);
        if (dep_it == entries_.end()) continue;
        auto& dep_entry = *dep_it->second;

        if (dep_entry.task.status != TaskStatus::Pending) continue;

        if (final_status == TaskStatus::Failed || final_status == TaskStatus::Cancelled) {
            dep_entry.task.status = TaskStatus::Cancelled;
            try {
                dep_entry.completion_promise.set_exception(
                    std::make_exception_ptr(std::runtime_error(
                        "Dependency " + std::to_string(id) + " failed or was cancelled")));
            } catch (...) {}
            continue;
        }

        size_t prev = dep_entry.remaining_deps.fetch_sub(1);
        if (prev == 1) {
            enqueue_ready(dep_id);
        }
    }

    // signal completion
    if (final_status == TaskStatus::Failed) {
        try {
            entry.completion_promise.set_exception(entry.task.error);
        } catch (...) {}
    } else if (final_status == TaskStatus::Cancelled) {
        try {
            entry.completion_promise.set_exception(
                std::make_exception_ptr(std::runtime_error("Task cancelled")));
        } catch (...) {}
    } else {
        try {
            entry.completion_promise.set_value();
        } catch (...) {}
    }

    cv_idle_.notify_all();
}

void TaskRunner::run_task(TaskId id) {
    TaskFunction func;
    {
        std::lock_guard<std::mutex> lk(mutex_);
        auto it = entries_.find(id);
        if (it == entries_.end()) return;
        if (it->second->task.status == TaskStatus::Cancelled) return;

        it->second->task.status = TaskStatus::Running;
        func = it->second->task.func;
    }

    active_tasks_++;
    {
        TaskStats s = stats_.load();
        s.currently_running++;
        stats_.store(s);
    }

    bool success = true;
    try {
        if (func) func();
    } catch (...) {
        success = false;
        std::lock_guard<std::mutex> lk(mutex_);
        auto it = entries_.find(id);
        if (it != entries_.end()) {
            it->second->task.error = std::current_exception();
        }
    }

    {
        TaskStats s = stats_.load();
        if (s.currently_running > 0) s.currently_running--;
        stats_.store(s);
    }
    active_tasks_--;

    {
        std::lock_guard<std::mutex> lk(mutex_);
        mark_complete(id, success ? TaskStatus::Completed : TaskStatus::Failed);
    }
}

bool TaskRunner::PriorityCompare::operator()(TaskId a, TaskId b) const {
    if (!entries) return false;
    auto it_a = entries->find(a);
    auto it_b = entries->find(b);
    if (it_a == entries->end() || it_b == entries->end()) return false;
    return it_a->second->task.priority > it_b->second->task.priority;
}

} // namespace apac
