// Copyright (c) RazorConsole. All rights reserved.

namespace RazorConsole.Testing.Pty;

/// <summary>
/// The <c>/bin/bash</c> command line and environment that start a program on macOS (see <see cref="UnixPtyConnection"/>).
/// </summary>
/// <remarks>
/// <para>
/// bash gets the caller's environment, minus the entries bash 3.2 itself acts on (<see cref="ActedOn"/>). argv is
/// readable by every user (<c>ps</c>) while the environment is readable only by its owner, so no other entry may
/// appear in argv. bash then execs <c>/usr/bin/env</c>, which puts the removed entries back, undoes bash's own changes
/// to the exported environment (<see cref="Rewritten"/>: the caller's value, or <c>-u</c> where the caller had none)
/// and execs the program. Only the values of those names reach argv. env(1) unsets every <c>-u</c> name, stops at
/// <c>--</c>, assigns each following argument containing <c>=</c>, then execs the rest.
/// </para>
/// <para>
/// bash re-exports every other imported variable unchanged: it keeps the original <c>name=value</c> string of each
/// entry, whatever the name, and exports that string. Locale variables are read but change neither the script nor
/// the exported values. <c>CDPATH</c> is not consulted because the directory is absolute.
/// </para>
/// </remarks>
internal static class BashLaunch
{
    public const string Bash = "/bin/bash";

    // $0 is the slave path; env(1)'s options and assignments, the program and its arguments follow.
    // 126: the terminal could not be attached or the directory changed.
    private const string AttachScript = "exec 0<>\"$0\" || exit 126; exec /usr/bin/env \"$@\"";
    private const string AttachAndChdirScript =
        "exec 0<>\"$0\" || exit 126; cd -P -- \"$1\" || exit 126; shift; exec /usr/bin/env \"$@\"";

    // Imported functions: bash 3.2 on macOS parses and runs a definition for an entry named BASH_FUNC_<name>%%
    // whose value starts with "() {", and exports it reformatted. Every such value is withheld, whatever the name.
    private const string FunctionPrefix = "() {";

    /// <summary>
    /// Gets the variables that change what bash 3.2 runs or how. They are withheld from bash and given to the program
    /// through env(1)'s argv.
    /// </summary>
    /// <remarks>
    /// <c>BASH_ENV</c>: sourced by a non-interactive shell. <c>ENV</c>: sourced by an interactive shell or one running
    /// as sh; neither applies here, but it is withheld all the same. <c>SHELLOPTS</c>: turned into <c>set -o</c>
    /// options such as <c>xtrace</c> or <c>allexport</c>. <c>POSIXLY_CORRECT</c> / <c>POSIX_PEDANTIC</c>: turn on
    /// posix mode. bash 3.2 has no <c>BASHOPTS</c>.
    /// </remarks>
    internal static readonly string[] ActedOn = ["BASH_ENV", "ENV", "SHELLOPTS", "POSIXLY_CORRECT", "POSIX_PEDANTIC"];

    /// <summary>
    /// Gets the variables whose exported value bash 3.2 changes, adds or drops before the program starts. env(1)
    /// restores the caller's value or unsets them.
    /// </summary>
    /// <remarks>
    /// At startup bash rebinds and exports <c>PWD</c> unless the imported value names the current directory, and turns
    /// <c>OLDPWD</c> into an invisible, valueless variable. It rebinds <c>IFS</c>, <c>OPTIND</c>, <c>OPTERR</c>,
    /// <c>BASH</c>, <c>BASH_VERSION</c> and <c>BASH_EXECUTION_STRING</c> (an imported one stays exported with the new
    /// value), exports <c>SHLVL</c> + 1 (<c>exec</c> subtracts 1 again, or exports 0 when the caller had none),
    /// unexports <c>PPID</c>, and replaces <c>BASH_VERSINFO</c> with an array, which is never exported.
    /// <c>BASH_COMMAND</c>, <c>BASH_SUBSHELL</c>, <c>RANDOM</c>, <c>LINENO</c>, <c>HISTCMD</c> and
    /// <c>COMP_WORDBREAKS</c> are rebound without a value. Running a command unexports <c>_</c>, and <c>cd</c> binds
    /// and exports <c>PWD</c> and <c>OLDPWD</c>. Variables bash only creates when missing (<c>PATH</c>, <c>TERM</c>,
    /// <c>PS4</c>, <c>SHELL</c>, <c>UID</c>, <c>SECONDS</c>, ...) are not exported unless imported, and then keep the
    /// imported value.
    /// </remarks>
    internal static readonly string[] Rewritten =
    [
        "PWD", "OLDPWD", "_", "IFS", "SHLVL", "PPID", "OPTIND", "OPTERR", "BASH", "BASH_VERSION", "BASH_VERSINFO",
        "BASH_EXECUTION_STRING", "BASH_COMMAND", "BASH_SUBSHELL", "RANDOM", "LINENO", "HISTCMD", "COMP_WORDBREAKS",
    ];

    /// <summary>
    /// Builds bash's argv, up to the program path (the program's arguments follow it), and bash's environment.
    /// </summary>
    /// <param name="program">The resolved program path; it cannot contain <c>=</c>, or env(1) would take it for an assignment.</param>
    /// <param name="slavePath">The PTY slave path.</param>
    /// <param name="workingDirectory">The directory bash changes to, or <see langword="null"/> to stay.</param>
    /// <param name="environment">The program's environment.</param>
    public static (string[] Arguments, string[] Environment) Build(
        string program, string slavePath, string? workingDirectory, IReadOnlyDictionary<string, string> environment)
    {
        var argv = new List<string> { Bash, "-c", workingDirectory is null ? AttachScript : AttachAndChdirScript, slavePath };
        if (workingDirectory is not null)
        {
            argv.Add(Path.GetFullPath(workingDirectory));
        }

        var envp = new List<string>(environment.Count);
        var assignments = new List<string>();
        foreach (var (name, value) in environment)
        {
            var withheld = ActedOn.Contains(name, StringComparer.Ordinal) || value.StartsWith(FunctionPrefix, StringComparison.Ordinal);
            if (!withheld)
            {
                envp.Add(name + "=" + value);
            }

            if (withheld || Rewritten.Contains(name, StringComparer.Ordinal))
            {
                assignments.Add(name + "=" + value);
            }
        }

        foreach (var name in Rewritten)
        {
            if (!environment.ContainsKey(name))
            {
                argv.AddRange(["-u", name]);
            }
        }

        argv.Add("--");
        argv.AddRange(assignments);
        argv.Add(program);
        return (argv.ToArray(), envp.ToArray());
    }
}
