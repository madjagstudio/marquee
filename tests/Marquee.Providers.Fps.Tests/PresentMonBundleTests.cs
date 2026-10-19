using System.Security.Cryptography;
using Marquee.Providers.Fps;

namespace Marquee.Providers.Fps.Tests;

/// PresentMon.targets sets the version, file name and hash once; the build bundles the exe from them and hands the same
/// values to the code as assembly metadata. The exe flows to this test project's output like it does to marquee.exe's,
/// so these tests check that the build and the code agree.
public class PresentMonBundleTests
{
    [Fact]
    public void The_file_name_and_hash_come_from_the_build()
    {
        Assert.Equal("PresentMon-2.6.0-x64.exe", PresentMonBundle.FileName);
        Assert.Matches("^[0-9a-f]{64}$", PresentMonBundle.Sha256);
    }

    [Fact]
    public void The_bundled_exe_sits_in_a_PresentMon_folder_next_to_the_app()
    {
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "PresentMon", PresentMonBundle.FileName), PresentMonBundle.DefaultPath);
    }

    [Fact]
    public void The_build_bundled_the_exe_the_code_expects()
    {
        Assert.True(File.Exists(PresentMonBundle.DefaultPath), $"{PresentMonBundle.DefaultPath} was not bundled.");

        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(PresentMonBundle.DefaultPath)));

        Assert.Equal(PresentMonBundle.Sha256, hash, ignoreCase: true);
        using (PresentMonBundle.OpenVerified(PresentMonBundle.DefaultPath, PresentMonBundle.Sha256)) { }
    }

    [Fact]
    public void A_verified_exe_cant_be_replaced_until_it_is_released()
    {
        var path = Path.Combine(Path.GetTempPath(), "marquee-test-" + Guid.NewGuid().ToString("N")[..8] + ".exe");
        File.WriteAllText(path, "pretend exe");
        try
        {
            var sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
            using (PresentMonBundle.OpenVerified(path, sha256))
            {
                // Held between the check and the start, so what starts is what was checked.
                Assert.Throws<IOException>(() => File.WriteAllText(path, "swapped"));
                Assert.Throws<IOException>(() => File.Delete(path));
            }

            File.WriteAllText(path, "free again");
        }
        finally
        {
            File.Delete(path);
        }
    }
}
