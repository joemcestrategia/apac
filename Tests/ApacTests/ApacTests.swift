import XCTest
@testable import Apac

final class MetricsCalculatorTests: XCTestCase {
    var calculator: MetricsCalculator!

    override func setUp() {
        calculator = MetricsCalculator()
    }

    func testEmptyRecords() {
        let summary = calculator.compute(from: [])
        XCTAssertEqual(summary.totalRecords, 0)
        XCTAssertEqual(summary.uniqueCountries, 0)
    }

    func testComputeMetrics() {
        let records = [
            makeRecord(country: "Singapore", department: "Engineering", employmentType: "Full-time"),
            makeRecord(country: "Singapore", department: "Engineering", employmentType: "Full-time"),
            makeRecord(country: "Japan", department: "Sales", employmentType: "Contractor"),
            makeRecord(country: "Australia", department: "Engineering", employmentType: "Full-time"),
            makeRecord(country: "Japan", department: "Marketing", employmentType: "Full-time"),
        ]

        let summary = calculator.compute(from: records)

        XCTAssertEqual(summary.totalRecords, 5)
        XCTAssertEqual(summary.uniqueCountries, 3)
        XCTAssertEqual(summary.uniqueDepartments, 3)

        let sg = summary.countryBreakdown.first { $0.country == "Singapore" }
        XCTAssertEqual(sg?.headcount, 2)

        let jp = summary.countryBreakdown.first { $0.country == "Japan" }
        XCTAssertEqual(jp?.headcount, 2)
    }

    private func makeRecord(country: String, department: String, employmentType: String) -> EmployeeRecord {
        EmployeeRecord(
            employeeID: UUID().uuidString,
            name: "Test User",
            country: country,
            timezone: "Asia/Singapore",
            department: department,
            jobTitle: "Engineer",
            employmentType: employmentType,
            city: "City",
            managerName: "Manager",
            importBatchID: "test",
            importedAt: Date()
        )
    }
}

final class CSVExporterTests: XCTestCase {
    var exporter: CSVExporter!

    override func setUp() {
        exporter = CSVExporter()
    }

    func testExportEmptyRecordsThrows() {
        let tmp = FileManager.default.temporaryDirectory
            .appendingPathComponent("test_empty.csv")
        XCTAssertThrowsError(try exporter.export(records: [], to: tmp))
    }
}

final class EmployeeRecordTests: XCTestCase {
    func testFromCellsValid() {
        let cells = ["E001", "Alice", "Singapore", "Asia/Singapore", "Engineering", "Engineer", "Full-time", "Singapore", "Bob"]
        let record = EmployeeRecord.from(cells: cells, batchID: "batch1")
        XCTAssertNotNil(record)
        XCTAssertEqual(record?.employeeID, "E001")
        XCTAssertEqual(record?.name, "Alice")
        XCTAssertEqual(record?.country, "Singapore")
    }

    func testFromCellsPartial() {
        let cells = ["E001", "Alice", "Singapore"]
        let record = EmployeeRecord.from(cells: cells, batchID: "batch1")
        XCTAssertNotNil(record)
        XCTAssertEqual(record?.employeeID, "E001")
        XCTAssertEqual(record?.department, "")
    }
}
