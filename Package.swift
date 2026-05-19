// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "Apac",
    platforms: [.macOS(.v14)],
    dependencies: [
        .package(url: "https://github.com/CoreOffice/CoreXLSX.git", from: "0.14.0"),
        .package(url: "https://github.com/groue/GRDB.swift.git", from: "6.29.0"),
    ],
    targets: [
        .executableTarget(
            name: "Apac",
            dependencies: [
                .product(name: "CoreXLSX", package: "CoreXLSX"),
                .product(name: "GRDB", package: "GRDB.swift"),
            ],
            path: "Sources/Apac",
            resources: [.process("../../Resources")]
        ),
        .testTarget(
            name: "ApacTests",
            dependencies: ["Apac"],
            path: "Tests/ApacTests"
        ),
    ]
)
