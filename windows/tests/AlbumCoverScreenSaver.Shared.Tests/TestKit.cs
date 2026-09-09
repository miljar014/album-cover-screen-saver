using System.Globalization;

namespace AlbumCoverScreenSaver.Shared.Tests;

public sealed class AssertionFailed(string message) : Exception(message);

/// <summary>A very small test runner, so the suite needs no packages.</summary>
public sealed class TestRunner
{
    private readonly List<(string Group, string Name, Action Body)> _tests = new();
    private string _group = "";

    public void Group(string name) => _group = name;

    public void Add(string name, Action body) => _tests.Add((_group, name, body));

    public int Run()
    {
        var failures = new List<string>();
        var lastGroup = "";
        var passed = 0;

        foreach (var (group, name, body) in _tests)
        {
            if (group != lastGroup)
            {
                Console.WriteLine();
                Console.WriteLine(group);
                Console.WriteLine(new string('-', group.Length));
                lastGroup = group;
            }

            try
            {
                body();
                passed++;
                Console.WriteLine($"  ok    {name}");
            }
            catch (Exception error)
            {
                var detail = error is AssertionFailed ? error.Message : $"{error.GetType().Name}: {error.Message}";
                failures.Add($"{group} / {name}: {detail}");
                Console.WriteLine($"  FAIL  {name}");
                Console.WriteLine($"        {detail}");
            }
        }

        Console.WriteLine();
        Console.WriteLine(new string('=', 60));
        Console.WriteLine($"{passed} passed, {failures.Count} failed, {_tests.Count} total");

        if (failures.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Failures:");
            foreach (var failure in failures) Console.WriteLine($"  - {failure}");
        }

        return failures.Count;
    }
}

public static class Check
{
    public static void True(bool value, string what)
    {
        if (!value) throw new AssertionFailed($"{what}: expected true, got false");
    }

    public static void False(bool value, string what)
    {
        if (value) throw new AssertionFailed($"{what}: expected false, got true");
    }

    public static void Equal<T>(T expected, T actual, string what)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new AssertionFailed($"{what}: expected {Show(expected)}, got {Show(actual)}");
        }
    }

    public static void NotEqual<T>(T unexpected, T actual, string what)
    {
        if (EqualityComparer<T>.Default.Equals(unexpected, actual))
        {
            throw new AssertionFailed($"{what}: expected anything other than {Show(unexpected)}");
        }
    }

    public static void Close(double expected, double actual, double tolerance, string what)
    {
        if (Math.Abs(expected - actual) > tolerance)
        {
            throw new AssertionFailed(
                $"{what}: expected {Show(expected)} within {Show(tolerance)}, got {Show(actual)}");
        }
    }

    public static void Sequence<T>(IEnumerable<T> expected, IEnumerable<T> actual, string what)
    {
        var left = expected.ToArray();
        var right = actual.ToArray();
        if (!left.SequenceEqual(right))
        {
            throw new AssertionFailed(
                $"{what}: expected [{string.Join(", ", left.Select(Show))}], " +
                $"got [{string.Join(", ", right.Select(Show))}]");
        }
    }

    public static void Contains(string needle, string haystack, string what)
    {
        if (!haystack.Contains(needle, StringComparison.Ordinal))
        {
            throw new AssertionFailed($"{what}: expected to find {Show(needle)} in {Show(haystack)}");
        }
    }

    public static void DoesNotContain(string needle, string haystack, string what)
    {
        if (haystack.Contains(needle, StringComparison.Ordinal))
        {
            throw new AssertionFailed($"{what}: did not expect to find {Show(needle)} in {Show(haystack)}");
        }
    }

    private static string Show<T>(T value) => value switch
    {
        null => "null",
        string text => $"\"{text}\"",
        double number => number.ToString("R", CultureInfo.InvariantCulture),
        DateTime moment => moment.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "null",
    };
}
