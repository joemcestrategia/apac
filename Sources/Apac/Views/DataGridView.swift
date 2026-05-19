import SwiftUI

struct DataGridView: View {
    @ObservedObject var viewModel: ApacViewModel

    var body: some View {
        VStack(spacing: 0) {
            summaryCards
                .padding(.horizontal)
                .padding(.top, 8)

            if viewModel.records.isEmpty {
                emptyState
            } else {
                tableContent
                    .padding(.horizontal)
            }
        }
    }

    private var summaryCards: some View {
        HStack(spacing: 12) {
            MetricCard(
                title: "Total Records",
                value: "\(viewModel.records.count)",
                color: .blue
            )
            MetricCard(
                title: "Countries",
                value: "\(viewModel.metricsSummary?.uniqueCountries ?? 0)",
                color: .green
            )
            MetricCard(
                title: "Departments",
                value: "\(viewModel.metricsSummary?.uniqueDepartments ?? 0)",
                color: .orange
            )
        }
    }

    private var emptyState: some View {
        VStack(spacing: 16) {
            Image(systemName: "rectangle.and.text.magnifyingglass")
                .font(.system(size: 48))
                .foregroundColor(.secondary)
            Text("No data to display")
                .font(.title2)
                .foregroundColor(.secondary)
            Text("Import an Excel file to get started.")
                .foregroundColor(.secondary)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
    }

    private var tableContent: some View {
        Table(viewModel.records) {
            TableColumn("Employee ID", value: \.employeeID)
                .width(min: 100, ideal: 120)

            TableColumn("Name") { record in
                Text(record.name)
                    .fontWeight(.medium)
            }
            .width(min: 120, ideal: 160)

            TableColumn("Country") { record in
                HStack {
                    Image(systemName: "globe.asia.australia")
                        .font(.caption)
                    Text(record.country)
                }
            }
            .width(min: 100, ideal: 120)

            TableColumn("Time zone", value: \.timezone)
                .width(min: 120, ideal: 150)

            TableColumn("Department", value: \.department)
                .width(min: 100, ideal: 140)

            TableColumn("Job Title", value: \.jobTitle)
                .width(min: 140, ideal: 200)

            TableColumn("Type") { record in
                Badge(text: record.employmentType)
            }
            .width(min: 80, ideal: 100)

            TableColumn("City", value: \.city)
                .width(min: 100, ideal: 130)

            TableColumn("Manager", value: \.managerName)
                .width(min: 120, ideal: 150)
        }
        .tableStyle(.bordered(alternatesRowBackgrounds: true))
    }
}

struct MetricCard: View {
    let title: String
    let value: String
    let color: Color

    var body: some View {
        VStack(spacing: 4) {
            Text(value)
                .font(.title2)
                .fontWeight(.bold)
                .foregroundColor(color)
            Text(title)
                .font(.caption)
                .foregroundColor(.secondary)
        }
        .frame(maxWidth: .infinity)
        .padding(.vertical, 10)
        .background(color.opacity(0.08))
        .clipShape(RoundedRectangle(cornerRadius: 8))
    }
}

struct Badge: View {
    let text: String

    var body: some View {
        Text(text)
            .font(.caption)
            .fontWeight(.medium)
            .padding(.horizontal, 8)
            .padding(.vertical, 2)
            .background(backgroundColor.opacity(0.15))
            .foregroundColor(backgroundColor)
            .clipShape(Capsule())
    }

    private var backgroundColor: Color {
        switch text.lowercased() {
        case "full-time": return .green
        case "contractor": return .orange
        case "part-time": return .purple
        default: return .gray
        }
    }
}
