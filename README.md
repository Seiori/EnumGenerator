# Seiori.EnumGenerator

A high-performance C\# Source Generator that automatically creates fast, zero-allocation extension methods for Enums.

## Installation

```bash
dotnet add package Seiori.EnumGenerator
```

## How To Use

1.  **Define your Enum.**
    The generator automatically detects your enum and creates the extensions in the background. You can use standard attributes like `[Display]`, `[Description]`, or `[EnumMember]`.

    ```csharp
    using System.ComponentModel.DataAnnotations;

    public enum Status
    {
        [Display(Name = "Ready to Start")]
        Pending,

        [Display(Name = "In Progress")]
        Active,

        Completed
    }
    ```

2.  **Call the Methods.**
    The generator creates a static class named `[YourEnum]Extensions`. Most methods are available directly on the enum value.

    ```csharp
    // 1. Get the code name (Faster than .ToString())
    string name = Status.Active.GetString(); // Returns "Active"

    // 2. Get the human-readable name (Reads [Display] or [Description])
    string display = Status.Pending.GetDisplayValue(); // Returns "Ready to Start"

    // 3. Fast Parsing (Zero allocation)
    if (StatusExtensions.TryParse("Active", out Status result))
    {
        // result is Status.Active
    }
    ```

## Available Methods

For an enum named `MyEnum`, the following methods are generated:

### 1\. Metadata & Collections

| Method | Description |
| :--- | :--- |
| `MyEnumExtensions.Length` | A constant `int` for the total number of enum members. |
| `GetValues()` | Returns all enum values as a `ReadOnlySpan<MyEnum>`. |
| `GetNames()` | Returns all enum names as a `ReadOnlySpan<string>`. |

### 2\. String Conversion

| Method | Description |
| :--- | :--- |
| `value.GetString()` | Returns the member name. Replaces `ToString()`. |
| `value.GetStringUpperCase()` | Returns the name in uppercase (e.g., "ACTIVE"). |
| `value.GetStringLowerCase()` | Returns the name in lowercase (e.g., "active"). |
| `value.GetDisplayValue()` | Returns the string from `[Display]`, `[Description]`, or the member name (in that priority). |

### 3\. Parsing

| Method | Description |
| :--- | :--- |
| `TryParse(string, out result)` | Fast string parsing. Returns `true` if successful. |
| `TryParse(ReadOnlySpan<char>, out result)` | Optimized parsing for Spans. |
| `Parse(ReadOnlySpan<char>)` | Parses or throws an `ArgumentException` on failure. |

### 4\. Utilities

| Method | Description |
| :--- | :--- |
| `IsDefined(value)` | Returns `true` if the integer value exists in the enum. |
| `GetUnderlyingValue()` | Returns the raw value (e.g., `int`) without boxing/casting. |
