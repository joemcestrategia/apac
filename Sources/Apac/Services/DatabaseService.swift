import Foundation
import GRDB

final class DatabaseService {
    private var dbQueue: DatabaseQueue?
    private let dbPath: String

    init(databaseName: String = "apac_data.sqlite") {
        let appSupport = FileManager.default.urls(
            for: .applicationSupportDirectory, in: .userDomainMask
        ).first!

        let dbDir = appSupport.appendingPathComponent("Apac")
        try? FileManager.default.createDirectory(at: dbDir, withIntermediateDirectories: true)

        self.dbPath = dbDir.appendingPathComponent(databaseName).path
    }

    func open() throws {
        dbQueue = try DatabaseQueue(path: dbPath)
        try createSchema()
    }

    func close() {
        dbQueue = nil
    }

    private func createSchema() throws {
        guard let dbQueue else { return }
        try dbQueue.write { db in
            try db.create(table: "employee_records", ifNotExists: true) { t in
                t.autoIncrementedPrimaryKey("id")
                t.column("employeeID", .text).notNull()
                t.column("name", .text).notNull()
                t.column("country", .text).notNull()
                t.column("timezone", .text).notNull()
                t.column("department", .text).notNull()
                t.column("jobTitle", .text).notNull()
                t.column("employmentType", .text).notNull()
                t.column("city", .text).notNull()
                t.column("managerName", .text).notNull()
                t.column("importBatchID", .text).notNull()
                t.column("importedAt", .datetime).notNull()
            }

            try db.create(indexOn: "employee_records", columns: ["country"])
            try db.create(indexOn: "employee_records", columns: ["department"])
            try db.create(indexOn: "employee_records", columns: ["employmentType"])
            try db.create(indexOn: "employee_records", columns: ["timezone"])
        }
    }

    func insertRecords(_ records: [EmployeeRecord]) throws -> Int {
        guard let dbQueue else { throw DatabaseError.notOpen }
        var count = 0
        try dbQueue.write { db in
            for record in records {
                try record.insert(db)
                count += 1
            }
        }
        return count
    }

    func fetchRecords(
        filter: FilterCriteria = FilterCriteria(),
        sort: SortDescriptor = SortDescriptor()
    ) throws -> [EmployeeRecord] {
        guard let dbQueue else { throw DatabaseError.notOpen }
        return try dbQueue.read { db in
            var query = EmployeeRecord.all()
            if let country = filter.country { query = query.filter(Column("country") == country) }
            if let dept = filter.department { query = query.filter(Column("department") == dept) }
            if let empType = filter.employmentType { query = query.filter(Column("employmentType") == empType) }
            if let tz = filter.timezone { query = query.filter(Column("timezone") == tz) }
            if !filter.searchText.isEmpty {
                let text = "%\(filter.searchText)%"
                query = query.filter(
                    Column("name").like(text) ||
                    Column("jobTitle").like(text) ||
                    Column("city").like(text)
                )
            }
            let column = Column(sort.field.rawValue)
            query = query.order(sort.ascending ? column.asc : column.desc)
            return try query.fetchAll(db)
        }
    }

    func fetchDistinctValues(column: String) throws -> [String] {
        guard let dbQueue else { throw DatabaseError.notOpen }
        return try dbQueue.read { db in
            try String.fetchAll(
                db,
                sql: "SELECT DISTINCT \(column) FROM employee_records ORDER BY \(column)"
            )
        }
    }

    func fetchCountryMetrics() throws -> [CountryMetrics] {
        guard let dbQueue else { throw DatabaseError.notOpen }
        return try dbQueue.read { db in
            try CountryMetrics.fetchAll(db, sql: """
                SELECT
                    country,
                    COUNT(*) AS headcount,
                    SUM(CASE WHEN employmentType = 'Full-time' THEN 1 ELSE 0 END) AS fullTimeCount,
                    SUM(CASE WHEN employmentType = 'Contractor' THEN 1 ELSE 0 END) AS contractorCount,
                    COUNT(DISTINCT department) AS departmentCount,
                    COUNT(DISTINCT timezone) AS uniqueTimezones
                FROM employee_records
                GROUP BY country
                ORDER BY headcount DESC
            """)
        }
    }

    func fetchDepartmentMetrics() throws -> [DepartmentMetrics] {
        guard let dbQueue else { throw DatabaseError.notOpen }
        return try dbQueue.read { db in
            try DepartmentMetrics.fetchAll(db, sql: """
                SELECT
                    department,
                    COUNT(*) AS headcount,
                    COUNT(DISTINCT country) AS countryCount
                FROM employee_records
                GROUP BY department
                ORDER BY headcount DESC
            """)
        }
    }

    func totalRecordCount() throws -> Int {
        guard let dbQueue else { throw DatabaseError.notOpen }
        return try dbQueue.read { db in
            try EmployeeRecord.fetchCount(db)
        }
    }

    func clearAll() throws {
        guard let dbQueue else { throw DatabaseError.notOpen }
        try dbQueue.write { db in
            try EmployeeRecord.deleteAll(db)
        }
    }

    enum DatabaseError: LocalizedError {
        case notOpen
        var errorDescription: String? { "Database is not open." }
    }
}
