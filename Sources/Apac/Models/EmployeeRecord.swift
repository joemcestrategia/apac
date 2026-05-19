import Foundation
import GRDB

struct EmployeeRecord: Identifiable, Codable, FetchableRecord, MutablePersistableRecord {
    var id: Int64?
    var employeeID: String
    var name: String
    var country: String
    var timezone: String
    var department: String
    var jobTitle: String
    var employmentType: String
    var city: String
    var managerName: String
    var importBatchID: String
    var importedAt: Date

    static var databaseTableName: String { "employee_records" }

    enum Columns: String, ColumnExpression {
        case id, employeeID, name, country, timezone, department
        case jobTitle, employmentType, city, managerName
        case importBatchID, importedAt
    }

    mutating func didInsert(_ inserted: InsertionSuccess) {
        id = inserted.rowID
    }
}

extension EmployeeRecord {
    static let sampleColumns = [
        "Employee ID", "Name", "Country", "Timezone", "Department",
        "Job Title", "Employment Type", "City", "Manager Name"
    ]

    static func from(cells: [String], batchID: String) -> EmployeeRecord? {
        guard cells[safe: 0] != nil else { return nil }
        return EmployeeRecord(
            employeeID: cells[safe: 0] ?? "",
            name: cells[safe: 1] ?? "",
            country: cells[safe: 2] ?? "",
            timezone: cells[safe: 3] ?? "",
            department: cells[safe: 4] ?? "",
            jobTitle: cells[safe: 5] ?? "",
            employmentType: cells[safe: 6] ?? "",
            city: cells[safe: 7] ?? "",
            managerName: cells[safe: 8] ?? "",
            importBatchID: batchID,
            importedAt: Date()
        )
    }
}

extension Array {
    subscript(safe index: Int) -> Element? {
        indices.contains(index) ? self[index] : nil
    }
}
