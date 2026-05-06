# File Explorer Example

A local file explorer application demonstrating how to build an interactive file browser in the console. This example shows how to:

- Browse directories and navigate the file system
- Display files and directories in a table
- Show file details dynamically
- Handle file I/O operations
- Update components in response to user interaction
- Use keyboard navigation for selection

## Running the Example

From the repository root:

```bash
dotnet run --project examples/FileExplorer
```

Or from this directory:

```bash
dotnet run
```

## Features Demonstrated

- **Directory Navigation**: Browse directories with clickable navigation
- **Parent Directory Access**: Navigate up the directory tree with a parent button
- **File/Directory Listing**: Display items in a formatted table with columns
- **File Details Panel**: Show detailed information when a file is selected
- **Dynamic Updates**: Components update automatically when navigating or selecting files
- **Error Handling**: Gracefully handles permission errors and inaccessible paths

## Key Components Used

- `Figlet` - ASCII art title
- `Panel` - Bordered containers with titles
- `SpectreTable` - Structured table display for file listings
- `Rows` - Vertical layout organization
- `Columns` - Horizontal layout for information display
- `TextButton` - Interactive buttons for directories and files
- `Markup` - Styled text with colors for different data types
- `Newline` - Spacing between elements

## Code Structure

- `FileExplorer.razor` - Main component with file browsing logic
- `Program.cs` - Application entry point
- `FileExplorer.csproj` - Project configuration using Microsoft.NET.Sdk.Razor

## Implementation Details

### File System Integration

The example demonstrates:
- Reading directories with `Directory.GetDirectories()` and `Directory.GetFiles()`
- Getting file metadata with `FileInfo` class
- Handling permission exceptions gracefully
- Formatting file sizes in human-readable format
- Displaying modification timestamps

### Table Component Usage

The table displays:
- **Name**: Clickable buttons for navigation/selection
- **Type**: Directory or File indicator
- **Size**: Human-readable file sizes (directories show "-")
- **Modified**: Last modification timestamp

### Dynamic Component Updates

- State changes trigger UI updates via `StateHasChanged()`
- File selection shows/hides the details panel
- Navigation refreshes the file list automatically

## Customization Ideas

- Add file filtering by extension or name
- Implement file search functionality
- Add sorting options (by name, size, date)
- Show file content preview for text files
- Add file operations (copy, move, delete)
- Display icons based on file types
- Add breadcrumb navigation
- Show hidden files toggle

## Technologies Used

- **RazorConsole.Core** - Console UI framework with Razor components
- **Spectre.Console** - Rich console rendering
- **System.IO** - File system operations
- **Microsoft.Extensions.Hosting** - Application hosting
