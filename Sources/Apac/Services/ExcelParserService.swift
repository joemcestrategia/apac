import Foundation
import CoreXLSX

struct ExcelParserService {
    struct ParseResult {
        let headers: [String]
        let rows: [[String]]
        let rowCount: Int
        let skippedCount: Int
    }

    enum ParseError: LocalizedError {
        case fileNotFound
        case invalidFormat
        case noSheets
        case emptySheet

        var errorDescription: String? {
            switch self {
            case .fileNotFound: return "Excel file not found at the specified path."
            case .invalidFormat: return "The file is not a valid Excel (.xlsx) file."
            case .noSheets: return "The workbook contains no sheets."
            case .emptySheet: return "The first sheet contains no data rows."
            }
        }
    }

    func parse(fileURL: URL) throws -> ParseResult {
        guard FileManager.default.fileExists(atPath: fileURL.path) else {
            throw ParseError.fileNotFound
        }

        guard let file = XLSXFile(filepath: fileURL.path) else {
            throw ParseError.invalidFormat
        }

        guard let sharedStrings = try file.parseSharedStrings() else {
            throw ParseError.invalidFormat
        }

        guard let sheetPath = try file.parseWorksheetPaths().first else {
            throw ParseError.noSheets
        }

        let worksheet = try file.parseWorksheet(at: sheetPath)

        guard let rows = worksheet.data?.rows, rows.count >= 2 else {
            throw ParseError.emptySheet
        }

        let headers = parseRow(rows[0], sharedStrings: sharedStrings)
        var dataRows: [[String]] = []
        var skipped: Int = 0

        for row in rows.dropFirst() {
            let cells = parseRow(row, sharedStrings: sharedStrings)
            let nonEmpty = cells.filter { !$0.trimmingCharacters(in: .whitespaces).isEmpty }
            if nonEmpty.isEmpty || nonEmpty.allSatisfy({ $0 == "" }) {
                skipped += 1
                continue
            }
            dataRows.append(cells)
        }

        return ParseResult(
            headers: headers,
            rows: dataRows,
            rowCount: dataRows.count,
            skippedCount: skipped
        )
    }

    private func parseRow(_ row: Row, sharedStrings: SharedStrings) -> [String] {
        return row.cells.map { cell in
            guard let reference = cell.reference else { return "" }
            switch cell.type {
            case .sharedString:
                if let index = cell.value.flatMap(Int.init),
                   index < sharedStrings.items.count {
                    return sharedStrings.items[index].text ?? ""
                }
                return cell.value ?? ""
            case .inlineStr:
                return cell.inlineString?.text ?? ""
            default:
                return cell.value ?? ""
            }
        }
    }
}
