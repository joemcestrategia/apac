import Foundation
import UniformTypeIdentifiers

final class CSVExporter {

    enum ExportError: LocalizedError {
        case writeFailed
        case noData

        var errorDescription: String? {
            switch self {
            case .writeFailed: return "Failed to write CSV file."
            case .noData: return "No data available to export."
            }
        }
    }

    func export(records: [EmployeeRecord], to url: URL) throws {
        guard !records.isEmpty else { throw ExportError.noData }

        let headers = [
            "Employee ID", "Name", "Country", "Timezone", "Department",
            "Job Title", "Employment Type", "City", "Manager Name"
        ]

        var csv = ""
        csv += headers.map { escape($0) }.joined(separator: ",") + "\n"

        for r in records {
            let row = [
                r.employeeID, r.name, r.country, r.timezone, r.department,
                r.jobTitle, r.employmentType, r.city, r.managerName
            ]
            csv += row.map { escape($0) }.joined(separator: ",") + "\n"
        }

        try csv.write(to: url, atomically: true, encoding: .utf8)
    }

    func exportMetrics(_ metrics: [CountryMetrics], to url: URL) throws {
        guard !metrics.isEmpty else { throw ExportError.noData }

        let headers = ["Country", "Headcount", "Full-time", "Contractors", "Departments", "Timezones"]
        var csv = headers.joined(separator: ",") + "\n"

        for m in metrics {
            let row = [
                m.country,
                String(m.headcount),
                String(m.fullTimeCount),
                String(m.contractorCount),
                String(m.departmentCount),
                String(m.uniqueTimezones)
            ]
            csv += row.map { escape($0) }.joined(separator: ",") + "\n"
        }

        try csv.write(to: url, atomically: true, encoding: .utf8)
    }

    private func escape(_ value: String) -> String {
        if value.contains(",") || value.contains("\"") || value.contains("\n") {
            let escaped = value.replacingOccurrences(of: "\"", with: "\"\"")
            return "\"\(escaped)\""
        }
        return value
    }
}
