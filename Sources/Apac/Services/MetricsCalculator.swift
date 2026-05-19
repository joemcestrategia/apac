import Foundation

final class MetricsCalculator {

    func compute(from records: [EmployeeRecord]) -> MetricsSummary {
        var countryCounts: [String: Int] = [:]
        var deptCounts: [String: Int] = [:]
        var empTypeCounts: [String: Int] = [:]
        var tzCounts: [String: Int] = [:]
        var countryDept: [String: Set<String>] = [:]
        var countryTZ: [String: Set<String>] = [:]

        for r in records {
            countryCounts[r.country, default: 0] += 1
            deptCounts[r.department, default: 0] += 1
            empTypeCounts[r.employmentType, default: 0] += 1
            tzCounts[r.timezone, default: 0] += 1

            countryDept[r.country, default: []].insert(r.department)
            countryTZ[r.country, default: []].insert(r.timezone)
        }

        let countryBreakdown = countryCounts.map { country, headcount in
            CountryBreakdown(
                country: country,
                headcount: headcount,
                departmentCount: countryDept[country]?.count ?? 0,
                uniqueTimezones: countryTZ[country]?.count ?? 0
            )
        }.sorted { $0.headcount > $1.headcount }

        return MetricsSummary(
            totalRecords: records.count,
            uniqueCountries: countryCounts.count,
            uniqueDepartments: deptCounts.count,
            countryBreakdown: countryBreakdown,
            departmentBreakdown: deptCounts.map { DepartmentBreakdown(name: $0.key, count: $0.value) }
                .sorted { $0.count > $1.count },
            employmentTypeBreakdown: empTypeCounts.map { EmploymentTypeBreakdown(type: $0.key, count: $0.value) }
                .sorted { $0.count > $1.count }
        )
    }
}

struct MetricsSummary {
    let totalRecords: Int
    let uniqueCountries: Int
    let uniqueDepartments: Int
    let countryBreakdown: [CountryBreakdown]
    let departmentBreakdown: [DepartmentBreakdown]
    let employmentTypeBreakdown: [EmploymentTypeBreakdown]
}

struct CountryBreakdown: Identifiable {
    var id: String { country }
    let country: String
    let headcount: Int
    let departmentCount: Int
    let uniqueTimezones: Int
}

struct DepartmentBreakdown: Identifiable {
    var id: String { name }
    let name: String
    let count: Int
}

struct EmploymentTypeBreakdown: Identifiable {
    var id: String { type }
    let type: String
    let count: Int
}
