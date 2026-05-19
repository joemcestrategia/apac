import Foundation
import Combine
import SwiftUI

@MainActor
final class ApacViewModel: ObservableObject {
    @Published var records: [EmployeeRecord] = []
    @Published var countryMetrics: [CountryMetrics] = []
    @Published var departmentMetrics: [DepartmentMetrics] = []
    @Published var metricsSummary: MetricsSummary?

    @Published var filter = FilterCriteria()
    @Published var sort = SortDescriptor()
    @Published var isImporting = false
    @Published var importMessage: String = ""
    @Published var importError: String?
    @Published var totalRecords: Int = 0

    @Published var filterCountries: [String] = []
    @Published var filterDepartments: [String] = []
    @Published var filterTimezones: [String] = []
    @Published var filterEmploymentTypes: [String] = []

    @Published var selectedTab: Tab = .dataGrid

    enum Tab: String, CaseIterable {
        case dataGrid = "Data Grid"
        case charts = "Charts"
    }

    private let db = DatabaseService()
    private let excelParser = ExcelParserService()
    private let metricsCalc = MetricsCalculator()
    private let csvExporter = CSVExporter()

    init() {
        do {
            try db.open()
            try refreshAll()
        } catch {
            importError = "Failed to open database: \(error.localizedDescription)"
        }
    }

    func importExcel(from url: URL) {
        isImporting = true
        importError = nil
        importMessage = ""

        Task {
            do {
                let result = try excelParser.parse(fileURL: url)
                let batchID = UUID().uuidString

                var validRecords: [EmployeeRecord] = []
                for cells in result.rows {
                    if let record = EmployeeRecord.from(cells: cells, batchID: batchID) {
                        validRecords.append(record)
                    }
                }

                guard !validRecords.isEmpty else {
                    importError = "No valid data rows found in the file."
                    isImporting = false
                    return
                }

                let inserted = try db.insertRecords(validRecords)
                let summary = metricsCalc.compute(from: validRecords)

                importMessage = "Imported \(inserted) records. "
                    + "\(summary.uniqueCountries) countries, "
                    + "\(summary.uniqueDepartments) departments."
                importError = nil

                try refreshAll()
            } catch {
                importError = error.localizedDescription
            }
            isImporting = false
        }
    }

    func refreshAll() throws {
        records = try db.fetchRecords(filter: filter, sort: sort)
        countryMetrics = try db.fetchCountryMetrics()
        departmentMetrics = try db.fetchDepartmentMetrics()
        totalRecords = try db.totalRecordCount()

        filterCountries = try db.fetchDistinctValues(column: "country")
        filterDepartments = try db.fetchDistinctValues(column: "department")
        filterTimezones = try db.fetchDistinctValues(column: "timezone")
        filterEmploymentTypes = try db.fetchDistinctValues(column: "employmentType")

        metricsSummary = metricsCalc.compute(from: records)
    }

    func applyFilters() {
        do {
            records = try db.fetchRecords(filter: filter, sort: sort)
            metricsSummary = metricsCalc.compute(from: records)
        } catch {
            importError = error.localizedDescription
        }
    }

    func updateSort(field: SortDescriptor.SortField) {
        if sort.field == field {
            sort.ascending.toggle()
        } else {
            sort.field = field
            sort.ascending = true
        }
        applyFilters()
    }

    func clearFilters() {
        filter = FilterCriteria()
        applyFilters()
    }

    func clearAllData() {
        do {
            try db.clearAll()
            try refreshAll()
            importMessage = "All data cleared."
        } catch {
            importError = error.localizedDescription
        }
    }

    func exportData(to url: URL) {
        do {
            try csvExporter.export(records: records, to: url)
        } catch {
            importError = "Export failed: \(error.localizedDescription)"
        }
    }

    func exportMetrics(to url: URL) {
        do {
            try csvExporter.exportMetrics(countryMetrics, to: url)
        } catch {
            importError = "Export failed: \(error.localizedDescription)"
        }
    }
}
