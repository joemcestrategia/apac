import Foundation
import GRDB

struct CountryMetrics: Identifiable, FetchableRecord {
    var id: Int64 { Int64(hashValue) }
    let country: String
    let headcount: Int
    let fullTimeCount: Int
    let contractorCount: Int
    let departmentCount: Int
    let uniqueTimezones: Int
}

struct DepartmentMetrics: Identifiable, FetchableRecord {
    var id: Int64 { Int64(hashValue) }
    let department: String
    let headcount: Int
    let countryCount: Int
}

struct FilterCriteria {
    var country: String?
    var department: String?
    var employmentType: String?
    var timezone: String?
    var searchText: String = ""

    var isEmpty: Bool {
        country == nil && department == nil && employmentType == nil && timezone == nil && searchText.isEmpty
    }
}

struct SortDescriptor {
    enum SortField: String, CaseIterable {
        case name, country, department, timezone, employmentType, jobTitle, city, managerName
    }
    var field: SortField = .name
    var ascending: Bool = true
}
