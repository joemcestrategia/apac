#pragma once

#include <atomic>
#include <chrono>
#include <condition_variable>
#include <functional>
#include <future>
#include <memory>
#include <mutex>
#include <queue>
#include <stdexcept>
#include <string>
#include <thread>
#include <unordered_map>
#include <vector>

namespace apac {

enum class TaskStatus {
    Pending,
    Ready,
    Running,
    Completed,
    Failed,
    Cancelled
};

const char* task_status_str(TaskStatus s);

using TaskId = int;
using TaskFunction = std::function<void()>;

struct Task {
    TaskId id;
    TaskFunction func;
    std::vector<TaskId> dependencies;
    int priority = 0;                    // lower = higher priority
    std::atomic<TaskStatus> status{TaskStatus::Pending};
    std::exception_ptr error;
    std::string name;

    Task() = default;
};

struct TaskStats {
    size_t total_submitted = 0;
    size_t total_completed = 0;
    size_t total_failed = 0;
    size_t total_cancelled = 0;
    size_t currently_running = 0;
    size_t currently_queued = 0;

    void reset();
    double completion_rate() const;
};

class TaskRunner {
public:
    explicit TaskRunner(size_t thread_count = 0);
    ~TaskRunner();

    TaskRunner(const TaskRunner&) = delete;
    TaskRunner& operator=(const TaskRunner&) = delete;
    TaskRunner(TaskRunner&&) = delete;
    TaskRunner& operator=(TaskRunner&&) = delete;

    TaskId submit(TaskFunction func,
                  std::vector<TaskId> dependencies = {},
                  int priority = 0,
                  std::string name = {});

    template <typename F, typename... Args>
    auto submit_with_result(F&& f, Args&&... args)
        -> std::future<decltype(f(std::forward<Args>(args)...))>;

    void submit_task(Task task);

    void run_all();
    void run_one();

    template <typename Rep, typename Period>
    void run_for(std::chrono::duration<Rep, Period> timeout);

    template <typename Clock, typename Duration>
    void run_until(std::chrono::time_point<Clock, Duration> deadline);

    void stop();
    void cancel(TaskId id);
    void cancel_all();
    void wait_all();
    void wait_one(TaskId id);

    TaskStatus status(TaskId id) const;
    TaskStats stats() const;
    size_t thread_count() const;
    size_t pending_count() const;
    size_t ready_count() const;

    std::exception_ptr task_error(TaskId id) const;
    void rethrow_if_error(TaskId id) const;

private:
    struct TaskEntry {
        Task task;
        std::atomic<size_t> remaining_deps{0};
        std::vector<TaskId> dependents;
        std::promise<void> completion_promise;
    };

    void worker_loop();
    void enqueue_ready(TaskId id);
    void mark_complete(TaskId id, TaskStatus final_status);
    void run_task(TaskId id);

    std::vector<std::thread> workers_;
    std::atomic<bool> stop_flag_{false};
    std::atomic<TaskId> next_id_{0};

    mutable std::mutex mutex_;
    std::condition_variable cv_work_;
    std::condition_variable cv_idle_;

    std::unordered_map<TaskId, std::unique_ptr<TaskEntry>> entries_;

    struct PriorityCompare {
        bool operator()(TaskId a, TaskId b) const;
        const std::unordered_map<TaskId, std::unique_ptr<TaskEntry>>* entries = nullptr;
    };

    std::priority_queue<TaskId, std::vector<TaskId>, PriorityCompare> ready_queue_;

    mutable std::atomic<TaskStats> stats_{};
    std::atomic<size_t> active_tasks_{0};
};

// ─── template implementations ────────────────────────────────────────────────

template <typename F, typename... Args>
auto TaskRunner::submit_with_result(F&& f, Args&&... args)
    -> std::future<decltype(f(std::forward<Args>(args)...))>
{
    using R = decltype(f(std::forward<Args>(args)...));
    auto promise = std::make_shared<std::promise<R>>();
    auto future = promise->get_future();

    TaskFunction task_func = [promise, f = std::forward<F>(f),
                              ... args = std::forward<Args>(args)]() mutable {
        try {
            if constexpr (std::is_void_v<R>) {
                f(std::forward<Args>(args)...);
                promise->set_value();
            } else {
                promise->set_value(f(std::forward<Args>(args)...));
            }
        } catch (...) {
            promise->set_exception(std::current_exception());
        }
    };

    submit(std::move(task_func));
    return future;
}

template <typename Rep, typename Period>
void TaskRunner::run_for(std::chrono::duration<Rep, Period> timeout) {
    run_until(std::chrono::steady_clock::now() + timeout);
}

template <typename Clock, typename Duration>
void TaskRunner::run_until(std::chrono::time_point<Clock, Duration> deadline) {
    while (std::chrono::steady_clock::now() < deadline) {
        {
            std::lock_guard<std::mutex> lk(mutex_);
            if (ready_queue_.empty() && active_tasks_ == 0) break;
        }
        std::unique_lock<std::mutex> lk(mutex_);
        cv_idle_.wait_until(lk, deadline, [this] {
            return ready_queue_.empty() && active_tasks_ == 0;
        });
    }
    stop();
    wait_all();
}

} // namespace apac
