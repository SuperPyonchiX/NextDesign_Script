public static class SnapshotLinkTests
{
    private static int checks;
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
    private static void Reject(Action action, string message) {
        try { action(); } catch (IOException) { checks++; return; } throw new Exception(message);
    }
    private static void Junction(string root, string path, string target, List<string> links) {
        if (!Path.GetFullPath(path).StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || !Path.GetFullPath(target).StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new Exception("Test junction outside fixture");
        var info = new ProcessStartInfo { FileName = "powershell.exe", UseShellExecute = false, CreateNoWindow = true,
            Arguments = "-NoProfile -Command \"$ErrorActionPreference='Stop'; New-Item -ItemType Junction -Path '"
                + path.Replace("'", "''") + "' -Target '" + target.Replace("'", "''") + "' | Out-Null\"" };
        using (var process = Process.Start(info)) { process.WaitForExit(); if (process.ExitCode != 0) throw new Exception("Test junction creation failed"); }
        links.Add(path);
    }
    public static void Run(string temp) {
        var area = Path.Combine(temp, "junction-fixture"); Directory.CreateDirectory(area);
        var real = Path.Combine(area, "real"); Directory.CreateDirectory(Path.Combine(real, "Attachment"));
        var source = Path.Combine(real, "Attachment", "資料.txt"); File.WriteAllText(source, "snapshot-before");
        var links = new List<string>();
        try {
            var alias = Path.Combine(area, "project-alias"); Junction(area, alias, real, links);
            var aliasFile = Path.Combine(alias, "Attachment", "資料.txt");
            Check(string.Equals(ReviewSnapshot.ResolvePath(aliasFile), source, StringComparison.OrdinalIgnoreCase), "Ancestor junction resolved");
            ReviewSnapshot.CheckFile(aliasFile); checks++;
            var output = Path.Combine(area, "output"); var inventory = new StringBuilder();
            ReviewSnapshot.CopyTree(Path.Combine(alias, "Attachment"), output, inventory, "Attachment");
            var copy = Path.Combine(output, "資料.txt");
            Check(File.ReadAllText(copy) == "snapshot-before", "Junction source copied");
            Check((File.GetAttributes(output) & FileAttributes.ReparsePoint) == 0 && (File.GetAttributes(copy) & FileAttributes.ReparsePoint) == 0, "Output contains ordinary files");
            File.WriteAllText(source, "snapshot-after");
            Check(File.ReadAllText(copy) == "snapshot-before", "Snapshot independent of source");
            Check(inventory.ToString().Contains(source), "Resolved provenance recorded");
            Reject(() => ReviewSnapshot.CopyTree(real, Path.Combine(alias, "nested-output"), new StringBuilder(), "x"), "Aliased descendant output accepted");
            Check(!Directory.Exists(Path.Combine(real, "nested-output")), "Invalid output not created");
            var other = Path.Combine(area, "other"); Directory.CreateDirectory(other); File.WriteAllText(Path.Combine(other, "extra.txt"), "extra");
            Junction(area, Path.Combine(real, "Attachment", "reference"), other, links);
            var withReference = Path.Combine(area, "with-reference");
            ReviewSnapshot.CopyTree(Path.Combine(alias, "Attachment"), withReference, new StringBuilder(), "Attachment");
            Check(File.ReadAllText(Path.Combine(withReference, "reference", "extra.txt")) == "extra", "Nested link materialized");
            Junction(area, Path.Combine(other, "cycle"), other, links);
            Reject(() => ReviewSnapshot.CopyTree(other, Path.Combine(area, "cycle-copy"), new StringBuilder(), "x"), "Cycle accepted");
            var returnSource = Path.Combine(area, "return-source"); Directory.CreateDirectory(returnSource);
            var returnOutput = Path.Combine(area, "return-output"); Directory.CreateDirectory(returnOutput);
            Junction(area, Path.Combine(returnSource, "output-link"), returnOutput, links);
            Reject(() => ReviewSnapshot.CopyTree(returnSource, returnOutput, new StringBuilder(), "x"), "Link back into output accepted");
            var target = Path.Combine(area, "vanishing"); Directory.CreateDirectory(target);
            var broken = Path.Combine(area, "broken"); Junction(area, broken, target, links);
            Directory.Delete(target, false); // Known empty fixture directory, never recursive.
            Reject(() => ReviewSnapshot.CopyTree(broken, Path.Combine(area, "broken-copy"), new StringBuilder(), "x"), "Broken link accepted");
        } finally {
            // Remove only the junction entries created above before the fixture's recursive cleanup.
            foreach (var path in links.AsEnumerable().Reverse()) Directory.Delete(path, false);
        }
        Console.WriteLine("PASS: " + checks + " real junction snapshot assertions.");
    }
}
