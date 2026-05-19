import SwiftUI
import UniformTypeIdentifiers

struct ContentView: View {
    @StateObject private var viewModel = ApacViewModel()

    var body: some View {
        NavigationSplitView {
            sidebar
        } detail: {
            tabContent
        }
        .frame(minWidth: 900, minHeight: 600)
        .toolbar { toolbarContent }
    }

    private var sidebar: some View {
        List(selection: $viewModel.selectedTab) {
            ForEach(ApacViewModel.Tab.allCases, id: \.self) { tab in
                Label(tab.rawValue, systemImage: tab == .dataGrid ? "tablecells" : "chart.bar.fill")
                    .tag(tab)
            }

            Divider()

            Section("Filters") {
                filterControls
            }

            Divider()

            Section("Actions") {
                importButton
                exportMenu
                Button(role: .destructive) {
                    viewModel.clearAllData()
                } label: {
                    Label("Clear All Data", systemImage: "trash")
                }
            }
        }
        .listStyle(.sidebar)
        .frame(minWidth: 240)
    }

    private var filterControls: some View {
        Group {
            Picker("Country", selection: Binding(
                get: { viewModel.filter.country ?? "" },
                set: { viewModel.filter.country = $0.isEmpty ? nil : $0; viewModel.applyFilters() }
            )) {
                Text("All").tag("")
                ForEach(viewModel.filterCountries, id: \.self) { Text($0).tag($0) }
            }

            Picker("Department", selection: Binding(
                get: { viewModel.filter.department ?? "" },
                set: { viewModel.filter.department = $0.isEmpty ? nil : $0; viewModel.applyFilters() }
            )) {
                Text("All").tag("")
                ForEach(viewModel.filterDepartments, id: \.self) { Text($0).tag($0) }
            }

            Picker("Time zone", selection: Binding(
                get: { viewModel.filter.timezone ?? "" },
                set: { viewModel.filter.timezone = $0.isEmpty ? nil : $0; viewModel.applyFilters() }
            )) {
                Text("All").tag("")
                ForEach(viewModel.filterTimezones, id: \.self) { Text($0).tag($0) }
            }

            Picker("Employment", selection: Binding(
                get: { viewModel.filter.employmentType ?? "" },
                set: { viewModel.filter.employmentType = $0.isEmpty ? nil : $0; viewModel.applyFilters() }
            )) {
                Text("All").tag("")
                ForEach(viewModel.filterEmploymentTypes, id: \.self) { Text($0).tag($0) }
            }

            TextField("Search name, title, city...", text: Binding(
                get: { viewModel.filter.searchText },
                set: { viewModel.filter.searchText = $0; viewModel.applyFilters() }
            ))
            .textFieldStyle(.roundedBorder)

            Button("Clear Filters") {
                viewModel.clearFilters()
            }
            .font(.caption)
        }
    }

    private var importButton: some View {
        Button {
            openFilePicker()
        } label: {
            Label("Import Excel...", systemImage: "square.and.arrow.down")
        }
        .disabled(viewModel.isImporting)
    }

    private var exportMenu: some View {
        Menu {
            Button("Export Data to CSV...") { saveCSV(type: .data) }
            Button("Export Metrics to CSV...") { saveCSV(type: .metrics) }
        } label: {
            Label("Export...", systemImage: "square.and.arrow.up")
        }
    }

    @ViewBuilder
    private var tabContent: some View {
        switch viewModel.selectedTab {
        case .dataGrid:
            DataGridView(viewModel: viewModel)
                .navigationTitle("APAC Timezone Data")
        case .charts:
            ChartsView(viewModel: viewModel)
                .navigationTitle("Headcount Comparison")
        }
    }

    @ToolbarContentBuilder
    private var toolbarContent: some ToolbarContent {
        ToolbarItem(placement: .navigation) {
            if viewModel.isImporting {
                ProgressView()
                    .scaleEffect(0.7)
                    .padding(.trailing, 4)
            }
            Text("Total records: \(viewModel.totalRecords)")
                .font(.caption)
                .foregroundColor(.secondary)
        }

        ToolbarItem(placement: .primaryAction) {
            if let error = viewModel.importError {
                HStack {
                    Image(systemName: "exclamationmark.triangle.fill")
                        .foregroundColor(.red)
                    Text(error)
                        .font(.caption)
                        .foregroundColor(.red)
                }
            } else if !viewModel.importMessage.isEmpty {
                Text(viewModel.importMessage)
                    .font(.caption)
                    .foregroundColor(.green)
            }
        }
    }

    private func openFilePicker() {
        let panel = NSOpenPanel()
        panel.allowedContentTypes = [UTType(filenameExtension: "xlsx")!]
        panel.allowsMultipleSelection = false
        panel.canChooseDirectories = false

        if panel.runModal() == .OK, let url = panel.url {
            viewModel.importExcel(from: url)
        }
    }

    private func saveCSV(type: CSVType) {
        let panel = NSSavePanel()
        panel.allowedContentTypes = [.commaSeparatedText]
        panel.nameFieldStringValue = type == .data ? "apac_data.csv" : "apac_metrics.csv"

        if panel.runModal() == .OK, let url = panel.url {
            switch type {
            case .data: viewModel.exportData(to: url)
            case .metrics: viewModel.exportMetrics(to: url)
            }
        }
    }

    private enum CSVType { case data, metrics }
}
