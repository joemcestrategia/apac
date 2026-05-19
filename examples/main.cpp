#include "task_runner.hpp"

#include <chrono>
#include <iostream>
#include <numeric>
#include <random>
#include <sstream>
#include <thread>

using namespace apac;
using namespace std::chrono;

void demo_simple() {
    std::cout << "\n=== Simple Task Execution ===\n";
    TaskRunner runner(4);
    std::atomic<int> counter{0};

    for (int i = 0; i < 20; ++i) {
        runner.submit([&counter, i] {
            std::this_thread::sleep_for(milliseconds(10 + rand() % 40));
            counter++;
        });
    }

    runner.stop();
    std::cout << "Completed " << counter << " tasks.\n";
}

void demo_priority() {
    std::cout << "\n=== Priority Scheduling ===\n";
    TaskRunner runner(1);

    std::mutex print_mtx;
    std::vector<int> order;

    // lower number = higher priority, should execute first
    for (int i = 0; i < 10; ++i) {
        runner.submit(
            [i, &print_mtx, &order] {
                std::lock_guard<std::mutex> lk(print_mtx);
                order.push_back(i);
            },
            {}, i);
    }

    runner.stop();
    std::cout << "Execution order (higher priority first): ";
    for (int x : order) std::cout << x << " ";
    std::cout << "\n";
}

void demo_dependencies() {
    std::cout << "\n=== Dependency Graph ===\n";

    // A -> C -> E
    // B -> D -> E
    TaskRunner runner(2);

    std::atomic<int> phase{0};
    std::mutex print_mtx;

    TaskId a = runner.submit(
        [&] {
            phase.fetch_add(1);
        },
        {}, 0, "A");

    TaskId b = runner.submit(
        [&] {
            phase.fetch_add(1);
        },
        {}, 0, "B");

    TaskId c = runner.submit(
        [&] {
            phase.fetch_add(1);
        },
        {a}, 0, "C");

    TaskId d = runner.submit(
        [&] {
            phase.fetch_add(1);
        },
        {b}, 0, "D");

    TaskId e = runner.submit(
        [&] {
            phase.fetch_add(1);
        },
        {c, d}, 0, "E");

    runner.stop();
    std::cout << "Dependency chain A,B->C,D->E completed: " << phase << " phases\n";
}

void demo_with_result() {
    std::cout << "\n=== Tasks with Results ===\n";
    TaskRunner runner(4);

    auto future = runner.submit_with_result([](int a, int b) { return a + b; }, 21, 21);
    // blocks until complete
    runner.stop();
    std::cout << "21 + 21 = " << future.get() << "\n";
}

void demo_stats() {
    std::cout << "\n=== Statistics ===\n";
    TaskRunner runner(4);

    for (int i = 0; i < 100; ++i) {
        runner.submit([] {
            std::this_thread::sleep_for(milliseconds(5));
        });
    }

    std::this_thread::sleep_for(milliseconds(100));
    auto s = runner.stats();
    std::cout << "Submitted: " << s.total_submitted
              << ", Completed: " << s.total_completed
              << ", Queued: " << s.currently_queued
              << ", Running: " << s.currently_running
              << ", Rate: " << (s.completion_rate() * 100) << "%\n";

    runner.stop();
    s = runner.stats();
    std::cout << "Final - Completed: " << s.total_completed
              << ", Rate: " << (s.completion_rate() * 100) << "%\n";
}

void demo_error_handling() {
    std::cout << "\n=== Error Handling ===\n";
    TaskRunner runner(2);

    auto bad_id = runner.submit([] {
        throw std::runtime_error("Something went wrong");
    });

    runner.stop();

    auto err = runner.task_error(bad_id);
    if (err) {
        try {
            std::rethrow_exception(err);
        } catch (const std::exception& e) {
            std::cout << "Caught task error: " << e.what() << "\n";
        }
    }

    // failing dependency cancels dependents
    TaskRunner runner2(2);
    auto dep = runner2.submit([] { throw std::runtime_error("dep failed"); });
    auto child = runner2.submit([] {}, {dep});

    runner2.stop();
    std::cout << "Child status after dep failure: "
              << task_status_str(runner2.status(child)) << "\n";
}

void demo_benchmark() {
    std::cout << "\n=== Micro-benchmark ===\n";
    const int N = 100000;

    {
        // serial
        auto start = high_resolution_clock::now();
        long long sum = 0;
        for (int i = 0; i < N; ++i) sum += i;
        auto elapsed = duration_cast<microseconds>(high_resolution_clock::now() - start).count();
        std::cout << "Serial:         " << elapsed << " us\n";
    }

    {
        // parallel
        TaskRunner runner;
        std::atomic<long long> sum{0};
        const int chunk = N / 10;

        auto start = high_resolution_clock::now();
        for (int i = 0; i < 10; ++i) {
            int begin = i * chunk;
            int end = (i == 9) ? N : begin + chunk;
            runner.submit([begin, end, &sum] {
                long long partial = 0;
                for (int j = begin; j < end; ++j) partial += j;
                sum.fetch_add(partial);
            });
        }
        runner.stop();
        auto elapsed = duration_cast<microseconds>(high_resolution_clock::now() - start).count();
        std::cout << "Parallel ("
                  << runner.thread_count() << " threads): " << elapsed << " us, sum = "
                  << sum << "\n";
    }
}

int main() {
    demo_simple();
    demo_priority();
    demo_dependencies();
    demo_with_result();
    demo_stats();
    demo_error_handling();
    demo_benchmark();
    return 0;
}
