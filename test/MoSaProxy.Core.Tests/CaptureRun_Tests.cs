using Shouldly;

namespace MoSaProxy.Tests;

public class CaptureRun_Tests {

	[Fact]
	public void DefaultRoot_IsAMoSaProxyCapturesFolderInTheSystemTempFolder() {

		// Given: nothing special

		// When: the default root is read
		string root = CaptureRun.DefaultRoot;

		// Then: it is MoSaProxyCaptures directly inside the system temp folder
		root.ShouldBe(Path.Combine(Path.GetTempPath(), "MoSaProxyCaptures"));
	}

	[Fact]
	public void Start_InAnEmptyRoot_CreatesARunFolderDirectlyInsideIt() {

		// Given: a root folder that does not exist yet
		using var folder = new TempFolder();
		string root = Path.Combine(folder.Path, "MoSaProxyCaptures");

		// When: a run starts
		using var run = CaptureRun.Start(root);

		// Then: the root now holds exactly one folder, and it is the run's
		Directory.GetDirectories(root).ShouldBe(new[] { run.Directory });
		Path.GetDirectoryName(run.Directory).ShouldBe(root);
	}

	[Fact]
	public void Dispose_DeletesTheRunFolderAndEverythingInIt() {

		// Given: a run that has written a capture file and a file inside a sub-folder
		using var folder = new TempFolder();
		var run = CaptureRun.Start(folder.Path);
		File.WriteAllText(Path.Combine(run.Directory, "1_c.txt"), "GET / HTTP/1.1");
		Directory.CreateDirectory(Path.Combine(run.Directory, "sub"));
		File.WriteAllText(Path.Combine(run.Directory, "sub", "x.txt"), "x");

		// When: the run is disposed
		run.Dispose();

		// Then: its folder is gone and no error is reported
		Directory.Exists(run.Directory).ShouldBeFalse();
		run.DeleteError.ShouldBeNull();
	}

	[Fact]
	public void Start_TwoRunsAtOnce_GetDifferentFolders() {

		// Given: a root folder
		using var folder = new TempFolder();

		// When: two runs start in it, in the same second
		using var first = CaptureRun.Start(folder.Path);
		using var second = CaptureRun.Start(folder.Path);

		// Then: they have different folders
		first.Directory.ShouldNotBe(second.Directory);
	}

	[Fact]
	public void Start_FolderOfARunThatIsGone_IsDeleted() {

		// Given: folders left by runs that were killed: one with a lock file nobody holds and a capture file, and one with no lock file
		using var folder = new TempFolder();
		string killed = Path.Combine(folder.Path, "20200101-000000");
		Directory.CreateDirectory(killed);
		File.WriteAllText(Path.Combine(killed, ".lock"), "");
		File.WriteAllText(Path.Combine(killed, "1_c.txt"), "GET / HTTP/1.1");
		string unlocked = Path.Combine(folder.Path, "20200101-000001");
		Directory.CreateDirectory(unlocked);
		File.WriteAllText(Path.Combine(unlocked, "1_c.txt"), "GET / HTTP/1.1");

		// When: a new run starts
		using var run = CaptureRun.Start(folder.Path);

		// Then: both leftovers are deleted and only the new run's folder remains
		Directory.GetDirectories(folder.Path).ShouldBe(new[] { run.Directory });
	}

	[Fact]
	public void Start_FolderOfARunThatIsStillLive_IsLeftAlone() {

		// Given: a run that is live and has written a capture file
		using var folder = new TempFolder();
		using var live = CaptureRun.Start(folder.Path);
		string captured = Path.Combine(live.Directory, "1_c.txt");
		File.WriteAllText(captured, "GET / HTTP/1.1");

		// When: another run starts in the same root
		using var other = CaptureRun.Start(folder.Path);

		// Then: the live run's folder and file are untouched
		File.ReadAllText(captured).ShouldBe("GET / HTTP/1.1");
		Directory.GetDirectories(folder.Path).Length.ShouldBe(2);
	}

	[Fact]
	public async Task Start_ManyRunsStartingTogether_NeverDeleteEachOthersFolders() {

		// Given: a root folder and eight runs that all start at the same moment
		using var folder = new TempFolder();
		const int count = 8;

		// When: they start in parallel
		var runs = await Task.WhenAll(Enumerable.Range(0, count).Select(_ => Task.Run(() => CaptureRun.Start(folder.Path))));

		// Then: every run's folder still exists, and each one is different
		try {
			runs.Select(r => r.Directory).Distinct().Count().ShouldBe(count);
			runs.ShouldAllBe(r => Directory.Exists(r.Directory));
			Directory.GetDirectories(folder.Path).Length.ShouldBe(count);
		}
		finally {
			foreach (var run in runs) run.Dispose();
		}
	}

	[Fact]
	public void Dispose_WhenAFileIsHeldOpen_ReportsTheErrorInsteadOfThrowingAndTheNextStartCleansUp() {

		// Given: a run with a capture file that a viewer holds open exclusively
		using var folder = new TempFolder();
		var run = CaptureRun.Start(folder.Path);
		var viewer = new FileStream(Path.Combine(run.Directory, "1_c.txt"), FileMode.Create, FileAccess.ReadWrite, FileShare.None);

		// When: the run is disposed
		run.Dispose();

		// Then: the folder could not be deleted and the reason is reported; once the viewer lets go, the next run's start deletes it
		run.DeleteError.ShouldNotBeNull();
		Directory.Exists(run.Directory).ShouldBeTrue();
		viewer.Dispose();
		using var next = CaptureRun.Start(folder.Path);
		Directory.GetDirectories(folder.Path).ShouldBe(new[] { next.Directory });
	}

	[Fact]
	public void Dispose_CalledTwice_IsHarmless() {

		// Given: a run that has been disposed
		using var folder = new TempFolder();
		var run = CaptureRun.Start(folder.Path);
		run.Dispose();

		// When: it is disposed again
		var again = () => run.Dispose();

		// Then: nothing is thrown and no error is reported
		Should.NotThrow(again);
		run.DeleteError.ShouldBeNull();
	}
}
