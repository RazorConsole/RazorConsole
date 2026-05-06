# Login Form Example

A CLI login screen that demonstrates form validation, input handling, and error highlighting in RazorConsole.

![login-portal](../../assets/example/login_portal.png)

## Features

This example showcases:

- **TextInput** for username entry with validation
- **TextInput with MaskInput** for secure password entry (acts as PasswordInput)
- **TextButton** for interactive actions (Login, Clear, Logout)
- **Input validation** with minimum length requirements
- **Error highlighting** with red borders and warning messages
- **Success feedback** with green panels and welcome message
- **State management** for login/logout flow

## Visual Preview

The login form displays:

```
 _                _         ____            _        _ 
| |    ___   __ _(_)_ __   |  _ \ ___  _ __| |_ __ _| |
| |   / _ \ / _` | | '_ \  | |_) / _ \| '__| __/ _` | |
| |__| (_) | (_| | | | | | |  __/ (_) | |  | || (_| | |
|_____\___/ \__, |_|_| |_| |_|   \___/|_|   \__\__,_|_|
            |___/                                       

╭─ User Login ─────────────────────────────────╮
│                                              │
│  ╭──────────────────────────────────────╮   │
│  │ Username  [Enter your username]      │   │
│  ╰──────────────────────────────────────╯   │
│                                              │
│  ╭──────────────────────────────────────╮   │
│  │ Password  [Enter your password]      │   │
│  ╰──────────────────────────────────────╯   │
│                                              │
│  [ Login ]  [ Clear ]                        │
│                                              │
╰──────────────────────────────────────────────╯

Press Tab to change focus • Press Enter to submit • Press Ctrl+C to exit
```

When validation fails (e.g., empty username):

```
╭─ User Login ─────────────────────────────────╮
│                                              │
│  ╭─────────────────────────────────────╮    │
│  │ ⚠ Username cannot be empty          │    │
│  ╰─────────────────────────────────────╯    │
│                                              │
│  ╭──────────────────────────────────────╮   │ ← Red border
│  │ Username  [Enter your username]      │   │
│  ╰──────────────────────────────────────╯   │
│  ⚠ Username is required                      │
│                                              │
│  ╭──────────────────────────────────────╮   │
│  │ Password  [••••••••]                 │   │
│  ╰──────────────────────────────────────╯   │
│                                              │
│  [ Login ]  [ Clear ]                        │
│                                              │
╰──────────────────────────────────────────────╯
```

After successful login:

```
╭─ User Login ─────────────────────────────────╮
│                                              │
│  ╭─────────────────────────────────────╮    │
│  │ ✓ Login Successful!                 │    │ ← Green border
│  │ Welcome, alice!                     │    │
│  ╰─────────────────────────────────────╯    │
│                                              │
│  [ Logout ]                                  │
│                                              │
╰──────────────────────────────────────────────╯
```

## Validation Rules

- Username must be at least 3 characters
- Password must be at least 6 characters
- Both fields are required

## Running the Example

```bash
cd examples/LoginForm
dotnet run
```

Use `Tab` to navigate between inputs and buttons, `Enter` to submit or click buttons, and `Ctrl+C` to exit.

## Components Used

- `Figlet` - ASCII art title
- `Panel` - Bordered containers for form and messages
- `TextInput` - Username and password input fields
- `TextButton` - Interactive buttons
- `Markup` - Styled text for labels and messages
- `Rows` and `Columns` - Layout components
- `Newline` - Spacing

## Key Implementation Details

### Password Input

Password masking is achieved using the `MaskInput` parameter on `TextInput`:

```razor
<TextInput Label="Password"
           Value="@_password"
           MaskInput="true"
           ... />
```

### Dynamic Error Highlighting

Input borders change color based on validation state:

```razor
FocusedBorderColor="@(_usernameError ? Color.Red : Color.DeepSkyBlue1)"
BorderColor="@(_usernameError ? Color.Red3 : Color.Grey37)"
```

### Validation Logic

The `HandleLogin` method validates inputs and displays appropriate error messages:

```csharp
private Task HandleLogin()
{
    if (string.IsNullOrWhiteSpace(_username))
    {
        _usernameError = true;
        _errorMessage = "Username cannot be empty";
        return Task.CompletedTask;
    }
    // ... more validation
}
```
