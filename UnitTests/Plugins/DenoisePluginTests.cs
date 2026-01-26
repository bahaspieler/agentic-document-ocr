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
    [TestClass]
    public class DenoisePluginTests
    {
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

            var outRef = await plugin.DenoiseImage(inputRef, method: "bilateral", strength: "medium");

            Assert.IsFalse(string.IsNullOrWhiteSpace(outRef));
            Assert.AreNotEqual(inputRef, outRef);

            var outPath = store.GetImagePath(outRef);
            Assert.IsTrue(File.Exists(outPath), $"Expected output file to exist: {outPath}");
        }

        [TestMethod]
        public async Task DenoiseImage_InvalidMethod_Throws()
        {
            using var temp = new TempFolder();
            var store = new TestImageStore(temp.Path);

            var inputRef = "img_noisy";
            var inputPath = Path.Combine(temp.Path, $"{inputRef}.png");

            using (var noisy = CreateNoisyImage(64, 64))
            {
                Cv2.ImWrite(inputPath, noisy);
            }

            store.RegisterExisting(inputRef, inputPath);

            var plugin = new DenoisePlugin(store);

            await ExpectThrowsAsync<ArgumentException>(() =>
                plugin.DenoiseImage(inputRef, method: "wrong", strength: "medium"));
        }

        [TestMethod]
        public async Task DenoiseImage_InvalidStrength_Throws()
        {
            using var temp = new TempFolder();
            var store = new TestImageStore(temp.Path);

            var inputRef = "img_noisy";
            var inputPath = Path.Combine(temp.Path, $"{inputRef}.png");

            using (var noisy = CreateNoisyImage(64, 64))
            {
                Cv2.ImWrite(inputPath, noisy);
            }

            store.RegisterExisting(inputRef, inputPath);

            var plugin = new DenoisePlugin(store);

            await ExpectThrowsAsync<ArgumentException>(() =>
                plugin.DenoiseImage(inputRef, method: "bilateral", strength: "super"));
        }

        private static async Task ExpectThrowsAsync<T>(Func<Task> action) where T : Exception
        {
            try
            {
                await action();
                Assert.Fail($"Expected exception of type {typeof(T).Name}, but no exception was thrown.");
            }
            catch (T)
            {
                // expected
            }
        }

        // --------------------
        // Helpers
        // --------------------

        private static Mat CreateNoisyImage(int width, int height)
        {
            var img = new Mat(height, width, MatType.CV_8UC3);
            var rng = new Random(123);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    img.Set(y, x, new Vec3b(
                        (byte)rng.Next(0, 256),
                        (byte)rng.Next(0, 256),
                        (byte)rng.Next(0, 256)
                    ));
                }
            }

            return img;
        }

        private sealed class TempFolder : IDisposable
        {
            public string Path { get; }

            public TempFolder()
            {
                Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "DenoiseTests_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path);
            }

            public void Dispose()
            {
                try { Directory.Delete(Path, recursive: true); } catch { /* ignore */ }
            }
        }

        /// <summary>
        /// Test implementation of IImageStore matching the interface members you reported:
        /// Exists, SaveFromFile, GetMetadata, SaveBytes (with nullable optional params), GetImagePath.
        /// </summary>
        private sealed class TestImageStore : IImageStore
        {
            private readonly string _root;
            private readonly Dictionary<string, string> _refToPath = new();
            private readonly Dictionary<string, IDictionary<string, string>> _refToMeta = new();

            public TestImageStore(string rootFolder)
            {
                _root = rootFolder;
            }

            public void RegisterExisting(string imageReference, string filePath, IDictionary<string, string>? metadata = null)
            {
                _refToPath[imageReference] = filePath;
                _refToMeta[imageReference] = metadata ?? new Dictionary<string, string>();
            }

            public bool Exists(string reference)
                => _refToPath.ContainsKey(reference);

            public string GetImagePath(string imageReference)
            {
                if (!_refToPath.TryGetValue(imageReference, out var path))
                    throw new FileNotFoundException($"Unknown image reference: {imageReference}");

                return path;
            }

            public IDictionary<string, string> GetMetadata(string reference)
            {
                if (_refToMeta.TryGetValue(reference, out var meta))
                    return meta;

                return new Dictionary<string, string>();
            }

            public string SaveFromFile(string path, string? desiredReference)
            {
                if (!File.Exists(path))
                    throw new FileNotFoundException($"Input file not found: {path}");

                var ext = System.IO.Path.GetExtension(path);
                var finalRef = string.IsNullOrWhiteSpace(desiredReference)
                    ? $"img_{Guid.NewGuid():N}"
                    : desiredReference;

                var destPath = System.IO.Path.Combine(_root, $"{finalRef}{ext}");
                File.Copy(path, destPath, overwrite: true);

                _refToPath[finalRef] = destPath;
                _refToMeta[finalRef] = new Dictionary<string, string>();
                return finalRef;
            }

            public string SaveBytes(byte[] bytes, string extension, IDictionary<string, string>? metadata = null, string? desiredReference = null)
            {
                var finalRef = string.IsNullOrWhiteSpace(desiredReference)
                    ? $"img_{Guid.NewGuid():N}"
                    : desiredReference;

                var filePath = System.IO.Path.Combine(_root, $"{finalRef}.{extension}");
                File.WriteAllBytes(filePath, bytes);

                _refToPath[finalRef] = filePath;
                _refToMeta[finalRef] = metadata ?? new Dictionary<string, string>();
                return finalRef;
            }
        }
    }
}
