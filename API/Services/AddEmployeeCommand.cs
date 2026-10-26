using System.Data.Common;
using System.Text;
using API.Database;
using API.Models;
using MySqlConnector;

namespace API.Services;

// Creates a manager account from the command line, for the first employee (nobody can
// sign in to the Employees tab until one exists):
//   dotnet run --project API -- add-employee
// Writes to whichever database DB_APP_CONNECTION in .env points at.
public static class AddEmployeeCommand
{
    public static async Task<int> RunAsync()
    {
        var server = TryGetServer();
        if (server == null)
        {
            Console.Error.WriteLine("DB_APP_CONNECTION isn't set in .env, so there's no database to add the employee to.");
            return 1;
        }
        Console.WriteLine("Adding a Staff Portal manager account to the database on " + server + ".");

        var input = new CreateEmployeeRequest
        {
            FirstName = Prompt("First name: "),
            LastName = Prompt("Last name: "),
            Email = Prompt("Email: "),
            Password = PromptHidden("Password (at least " + EmployeeStore.MinPasswordLength + " characters): "),
            Role = EmployeeStore.Manager
        };
        if (PromptHidden("Password again: ") != input.Password)
        {
            Console.Error.WriteLine("The passwords didn't match. Nothing was saved.");
            return 1;
        }

        try
        {
            var employee = await new EmployeeStore(new Db()).CreateAsync(input);
            Console.WriteLine("Added " + employee.FirstName + " " + employee.LastName + " (" + employee.Email + "). They can sign in at #/staff.");
            return 0;
        }
        catch (HotelRuleException e)
        {
            Console.Error.WriteLine(e.Message + " Nothing was saved.");
        }
        catch (Exception e) when (e is DbException or DatabaseUnavailableException)
        {
            // Never print the connection string or SQL.
            Console.Error.WriteLine("Couldn't save to the database (" + e.GetType().Name + "). " +
                "Check that MySQL is running, DB_APP_CONNECTION is right, and migrate-004 and migrate-005 have been run.");
        }
        return 1;
    }

    // Only the host name, never the user or password.
    private static string? TryGetServer()
    {
        var connection = Environment.GetEnvironmentVariable("DB_APP_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return null;
        try
        {
            return new MySqlConnectionStringBuilder(connection).Server;
        }
        catch (ArgumentException)
        {
            return "an unreadable DB_APP_CONNECTION";
        }
    }

    private static string Prompt(string label)
    {
        Console.Write(label);
        return Console.ReadLine() ?? "";
    }

    // Reads a password without echoing it.
    private static string PromptHidden(string label)
    {
        Console.Write(label);
        if (Console.IsInputRedirected) return Console.ReadLine() ?? "";
        var text = new StringBuilder();
        for (;;)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) break;
            if (key.Key == ConsoleKey.Backspace) { if (text.Length > 0) text.Length--; }
            else if (!char.IsControl(key.KeyChar)) text.Append(key.KeyChar);
        }
        Console.WriteLine();
        return text.ToString();
    }
}
