# GitIgnoreCleaner <img src="assets/icon.png" align="right" width="128" height="128" />

GitIgnoreCleaner is a Windows utility built with WinUI 3 that helps you reclaim disk space by finding and deleting files ignored by your version control system.

![GitIgnoreCleaner Screenshot](assets/screenshot.png)

It scans a directory tree, parses `.gitignore` (and `.ignore`) files found at various levels, and identifies files that match these rules typically build artifacts (`bin/`, `obj/`), temporary files, and logs.

## Features

- **Recursive Scanning**: Respects nested `.gitignore` files, correctly applying rules to subdirectories.
- **Visual Preview**: See exactly what will be deleted before you commit to it.
- **Compact Folders**: Automatically flattens directory hierarchies where folders contain only a single child, making navigation easier.
- **Smart Deletion**: 
  - Move to Recycle Bin (default) for safety.
  - Permanently delete for speed and maximum space recovery.
- **File Inspection**: 
  - Open files or folders in Explorer.
  - View the specific ignore rule that matched a file.
  - Open the source `.gitignore` file for any match.

## Requirements

- Windows 10 version 1809 (Build 17763) or later.
- .NET 8 Runtime.

## Getting Started

1. Launch the application.
2. Select a **Root folder** to scan (e.g., your repositories folder).
3. (Optional) Customize the ignore file names to look for (default: `.gitignore;.ignore`).
4. Click **Scan**.
5. Review the results in the tree view.
   - Items marked with a tag icon are explicitly matched by a rule.
   - Folder sizes are calculated automatically.
6. Select the items you wish to remove (or select all).
7. Click **Delete Selected**.

## Development

This project is built using:
- **C# 12**
- **.NET 8**
- **WinUI 3** (Windows App SDK)

### Building

1. Open the solution in Visual Studio 2022.
2. Ensure the "Windows App SDK" workload is installed.
3. Build and run the `GitIgnoreCleaner` project.

## Acknowledgments

This project was developed with the assistance of AI tools, including GitHub Copilot, to accelerate development and refine the user interface.
