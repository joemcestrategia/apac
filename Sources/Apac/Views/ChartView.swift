import SwiftUI
import Charts

struct ChartsView: View {
    @ObservedObject var viewModel: ApacViewModel

    var body: some View {
        ScrollView {
            VStack(spacing: 20) {
                if viewModel.countryMetrics.isEmpty {
                    emptyChartState
                } else {
                    headcountByCountryChart
                        .frame(height: max(300, CGFloat(viewModel.countryMetrics.count * 30 + 100)))

                    Divider()

                    headcountByDepartmentChart
                        .frame(height: max(300, CGFloat(viewModel.departmentMetrics.count * 30 + 100)))

                    Divider()

                    employmentTypePieChart
                        .frame(height: 300)
                }
            }
            .padding()
        }
    }

    private var emptyChartState: some View {
        VStack(spacing: 16) {
            Image(systemName: "chart.bar.fill")
                .font(.system(size: 48))
                .foregroundColor(.secondary)
            Text("No data to chart")
                .font(.title2)
                .foregroundColor(.secondary)
            Text("Import an Excel file to see charts.")
                .foregroundColor(.secondary)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .padding(.top, 100)
    }

    private var headcountByCountryChart: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text("Headcount by Country")
                .font(.headline)

            Chart(viewModel.countryMetrics) { metric in
                BarMark(
                    x: .value("Headcount", metric.headcount),
                    y: .value("Country", metric.country)
                )
                .foregroundStyle(by: .value("Country", metric.country))
                .annotation(position: .trailing) {
                    Text("\(metric.headcount)")
                        .font(.caption2)
                        .foregroundColor(.secondary)
                }
            }
            .chartXAxis {
                AxisMarks(position: .bottom) { _ in
                    AxisGridLine()
                    AxisValueLabel()
                }
            }
            .chartLegend(.hidden)
        }
        .padding()
        .background(Color(nsColor: .controlBackgroundColor))
        .clipShape(RoundedRectangle(cornerRadius: 10))
    }

    private var headcountByDepartmentChart: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text("Headcount by Department")
                .font(.headline)

            Chart(viewModel.departmentMetrics) { metric in
                BarMark(
                    x: .value("Headcount", metric.headcount),
                    y: .value("Department", metric.department)
                )
                .foregroundStyle(.orange.gradient)
                .annotation(position: .trailing) {
                    Text("\(metric.headcount)")
                        .font(.caption2)
                        .foregroundColor(.secondary)
                }
            }
            .chartXAxis {
                AxisMarks(position: .bottom) { _ in
                    AxisGridLine()
                    AxisValueLabel()
                }
            }
            .chartLegend(.hidden)
        }
        .padding()
        .background(Color(nsColor: .controlBackgroundColor))
        .clipShape(RoundedRectangle(cornerRadius: 10))
    }

    private var employmentTypePieChart: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text("Employment Type Distribution")
                .font(.headline)

            if let summary = viewModel.metricsSummary {
                Chart(summary.employmentTypeBreakdown) { item in
                    SectorMark(
                        angle: .value("Count", item.count),
                        innerRadius: .ratio(0.5),
                        angularInset: 1.5
                    )
                    .foregroundStyle(by: .value("Type", item.type))
                    .annotation(position: .overlay) {
                        Text("\(item.count)")
                            .font(.caption)
                            .foregroundColor(.white)
                            .fontWeight(.bold)
                    }
                }
                .chartLegend(position: .bottom, spacing: 16)
            }
        }
        .padding()
        .background(Color(nsColor: .controlBackgroundColor))
        .clipShape(RoundedRectangle(cornerRadius: 10))
    }
}
