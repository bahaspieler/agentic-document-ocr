using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OcrAgent.Plugins.Enhancement;
using OcrAgent.Storage;
using OpenCvSharp;

namespace UnitTests.Plugins.Enhancement
{
    /// <summary>
    /// Tests DenoisePlugin bilateral, median, and error handling.
    /// </summary>
    [TestClass]
    public class DenoisePluginTests
    {
        // --------------------
        // Happy-path tests
        // --------------------

        [TestMethod]
        public async Task DenoiseImage_Bilateral_Medium_CreatesNewImage()
        {
            using var temp = new TempFolder();
            var store = new TestImageStore(temp.Path);

            var inputRef = "img_noisy";
            var inputPath = Path.Combine(temp.Path, $"{inputRef}.png");

            using (var noisy = CreateNoisyImage(128, 128))
            {
                Cv2.ImWrite(inputPath, noisy);
            }

            store.RegisterExisting(inputRef, inputPath);
            var plugin = new DenoisePlugin(store);

            var outRef = await plugin.DenoiseImage(inputRef, "bilateral", "medium");

            Assert.IsFalse(string.IsNullOrWhiteSpace(outRef));
            Assert.AreNotEqual(inputRef, outRef);
            Assert.IsTrue(File.Exists(store.GetImagePath(outRef)));
        }

        [TestMethod]
        public async Task DenoiseImage_Median_Low_CreatesNewImage()
        {
            using var temp = new TempFolder();
            var store = new TestImageStore(temp.Path);

            var inputRef = "img_noisy";
            var inputPath = Path.Combine(temp.Path, $"{inputRef}.png");

            using (var noisy = CreateNoisyImage(128, 128))
            {
                Cv2.ImWrite(inputPath, noisy);
            }

            store.RegisterExisting(inputRef, inputPath);
            var plugin = new DenoisePlugin(store);

            var outRef = await plugin.DenoiseImage(inputRef, "median", "low");

            Assert.IsTrue(File.Exists(store.GetImagePath(outRef)));
        }

        [TestMethod]
        public async Task DenoiseImage_Nlm_High_CreatesNewImage()
        {
            using var temp = new TempFolder();
            var store = new TestImageStore(temp.Path);

            var inputRef = "img_noisy";
            var inputPath = Path.Combine(temp.Path, $"{inputRef}.png");

            using (var noisy = CreateNoisyImage(128, 128))
            {
                Cv2.ImWrite(inputPath, noisy);
            }

            store.RegisterExisting(inputRef, inputPath);
            var plugin = new DenoisePlugin(store);

            var outRef = await plugin.DenoiseImage(inputRef, "nlm", "high");

            Assert.IsTrue(File.Exists(store.GetImagePath(outRef)));
        }

        // --------------------
        // Auto mode tests
        // --------------------

        [TestMethod]
        public async Task DenoiseImage_Auto_SelectsMethodBasedOnNoise()
        {
            using var temp = new TempFolder();
            var store = new TestImageStore(temp.Path);

            var inputRef = "img_noisy";
            var inputPath = Path.Combine(temp.Path, $"{inputRef}.png");

            using (var noisy = CreateNoisyImage(128, 128))
            {
                Cv2.ImWrite(inputPath, noisy);
            }

            store.RegisterExisting(inputRef, inputPath);
            var plugin = new DenoisePlugin(store);

            var outRef = await plugin.DenoiseImage(inputRef, "auto");

            Assert.IsFalse(string.IsNullOrWhiteSpace(outRef));
            Assert.IsTrue(File.Exists(store.GetImagePath(outRef)));
        }

        [TestMethod]
        public async Task DenoiseImage_Auto_WithQualityProfile_UsesPrecomputedNoise()
        {
            using var temp = new TempFolder();
            var store = new TestImageStore(temp.Path);

            var inputRef = "img_noisy";
            var inputPath = Path.Combine(temp.Path, $"{inputRef}.png");

            using (var noisy = CreateNoisyImage(128, 128))
            {
                Cv2.ImWrite(inputPath, noisy);
            }

            store.RegisterExisting(inputRef, inputPath);
            var plugin = new DenoisePlugin(store);

            var qualityProfile = """{"noiseSigma": 20.0}""";
            var outRef = await plugin.DenoiseImage(inputRef, "auto", qualityProfile: qualityProfile);

            Assert.IsFalse(string.IsNullOrWhiteSpace(outRef));
            Assert.AreNotEqual(inputRef, outRef);
        }

        [TestMethod]
        public async Task DenoiseImage_Auto_SkipsWhenNoiseNegligible()
        {
            using var temp = new TempFolder();
            var store = new TestImageStore(temp.Path);

            var inputRef = "img_clean";
            var inputPath = Path.Combine(temp.Path, $"{inputRef}.png");

            // Create a clean (no noise) image
            using (var clean = new Mat(128, 128, MatType.CV_8UC3, Scalar.White))
            {
                Cv2.ImWrite(inputPath, clean);
            }

            store.RegisterExisting(inputRef, inputPath);
            var plugin = new DenoisePlugin(store);

            var qualityProfile = """{"noiseSigma": 2.0}""";
            var outRef = await plugin.DenoiseImage(inputRef, "auto", qualityProfile: qualityProfile);

            Assert.AreEqual(inputRef, outRef, "Should skip when noise is negligible");
        }

        // --------------------
        // Validation tests
        // --------------------

        [TestMethod]
        public async Task DenoiseImage_InvalidMethod_Throws()
        {
            using var temp = new TempFolder();
            var store = new TestImageStore(temp.Path);

            var inputRef = "img";
            var inputPath = Path.Combine(temp.Path, $"{inputRef}.png");

            using (var noisy = CreateNoisyImage(64, 64))
            {
                Cv2.ImWrite(inputPath, noisy);
            }

            store.RegisterExisting(inputRef, inputPath);
            var plugin = new DenoisePlugin(store);

            await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
                plugin.DenoiseImage(inputRef, "wrong", "medium"));
        }

        [TestMethod]
        public async Task DenoiseImage_InvalidStrength_Throws()
        {
            using var temp = new TempFolder();
            var store = new TestImageStore(temp.Path);

            var inputRef = "img";
            var inputPath = Path.Combine(temp.Path, $"{inputRef}.png");

            using (var noisy = CreateNoisyImage(64, 64))
            {
                Cv2.ImWrite(inputPath, noisy);
            }

            store.RegisterExisting(inputRef, inputPath);
            var plugin = new DenoisePlugin(store);

            await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
                plugin.DenoiseImage(inputRef, "bilateral", "super"));
        }

        // --------------------
        // Behaviour test
        // --------------------

        [TestMethod]
        public async Task DenoiseImage_ActuallyChangesPixels_ForNoisyInput()
        {
            using var temp = new TempFolder();
            var store = new TestImageStore(temp.Path);

            var inputRef = "img_noisy";
            var inputPath = Path.Combine(temp.Path, $"{inputRef}.png");

            using (var noisy = CreateNoisyImage(128, 128))
            {
                Cv2.ImWrite(inputPath, noisy);
            }

            store.RegisterExisting(inputRef, inputPath);
            var plugin = new DenoisePlugin(store);

            var outRef = await plugin.DenoiseImage(inputRef, "bilateral", "high");

            using var before = Cv2.ImRead(inputPath);
            using var after = Cv2.ImRead(store.GetImagePath(outRef));

            using var diff = new Mat();
            Cv2.Absdiff(before, after, diff);

            var sum = Cv2.Sum(diff);
            var totalDiff = sum.Val0 + sum.Val1 + sum.Val2;

            Assert.IsGreaterThan(0.0, totalDiff, "Expected denoising to modify pixel values.");
        }

        // --------------------
        // Helpers
        // --------------------


        private static Mat CreateNoisyImage(int width, int height)
        {
            var img = new Mat(height, width, MatType.CV_8UC3);
            var rng = new Random(123);

            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    img.Set(y, x, new Vec3b(
                        (byte)rng.Next(256),
                        (byte)rng.Next(256),
                        (byte)rng.Next(256)));

            return img;
        }

        private sealed class TempFolder : IDisposable
        {
            public string Path { get; }

            public TempFolder()
            {
                Path = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    "DenoiseTests_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path);
            }

            public void Dispose()
            {
                try { Directory.Delete(Path, true); } catch { }
            }
        }

        /// <summary>
        /// Test implementation of IImageStore matching your real interface
        /// </summary>
        private sealed class TestImageStore : IImageStore
        {
            private readonly string _root;
            private readonly Dictionary<string, string> _paths = new();
            private readonly Dictionary<string, IDictionary<string, string>> _meta = new();

            public TestImageStore(string root)
            {
                _root = root;
            }

            public void RegisterExisting(string reference, string path)
            {
                _paths[reference] = path;
                _meta[reference] = new Dictionary<string, string>();
            }

            public bool Exists(string reference) => _paths.ContainsKey(reference);

            public string GetImagePath(string reference)
            {
                if (!_paths.TryGetValue(reference, out var path))
                    throw new FileNotFoundException(reference);
                return path;
            }

            public IDictionary<string, string> GetMetadata(string reference)
                => _meta.TryGetValue(reference, out var m) ? m : new Dictionary<string, string>();

            public string SaveFromFile(string path, string? desiredReference)
            {
                var ext = Path.GetExtension(path);
                var finalRef = desiredReference ?? $"img_{Guid.NewGuid():N}";
                var dest = Path.Combine(_root, finalRef + ext);
                File.Copy(path, dest, true);
                _paths[finalRef] = dest;
                _meta[finalRef] = new Dictionary<string, string>();
                return finalRef;
            }

            public string SaveBytes(byte[] bytes, string extension, IDictionary<string, string>? metadata = null, string? desiredReference = null)
            {
                var finalRef = desiredReference ?? $"img_{Guid.NewGuid():N}";
                var dest = Path.Combine(_root, $"{finalRef}.{extension}");
                File.WriteAllBytes(dest, bytes);
                _paths[finalRef] = dest;
                _meta[finalRef] = metadata ?? new Dictionary<string, string>();
                return finalRef;
            }

            public void SetActiveSubdirectory(string? subdirectory) { }
            public string? GetActiveSubdirectory() => null;
            public string GetWriteDirectory() => _root;
        }
    }
}
