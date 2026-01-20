using Chords.Entities;
using Chords.Predictors;
using Chords.Profiling;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.IO;

namespace ChordREcognizer2k.Pages
{
    public class PageViewModel
    {
        public string Chords { get; set; }
    }

    public class IndexModel : PageModel
    {
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<IndexModel> _logger;

        public IndexModel(ILogger<IndexModel> logger, IWebHostEnvironment env)
        {
            _logger = logger;
            _env = env;
        }

        private IPredictor _predictor = new AutoMlPredictor("D:\\Projects\\NeuralChordRecognition\\Chords.net\\Chords\\models\\model1595137632S120L0.004830031641869656.model");
        private Chord[] chordsPredicted;
        private int[] chordsIntervals;

        [BindProperty]
        public IFormFile? LastUploadedFile { get; set; }
        [BindProperty]
        public string? UploadedFileUrl { get; set; }

        [HttpPost]
        public async Task<IActionResult> OnPostAddFile()
        {
            if (LastUploadedFile != null)
            {
                using var inputStream = LastUploadedFile.OpenReadStream();
                if (!IsAudioFile(inputStream))
                {
                    LastUploadedFile = null;
                    return Page();
                }

                var uploadsPath = Path.Combine(_env.WebRootPath, "uploads");
                Directory.CreateDirectory(uploadsPath);
                var filePath = Path.Combine(uploadsPath, LastUploadedFile.FileName);
                using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    await LastUploadedFile.CopyToAsync(fileStream);
                    UploadedFileUrl = Path.Combine("uploads", LastUploadedFile.FileName);
                }
            }

            return Page();
        }

        public static bool IsAudioFile(Stream stream)
        {
            byte[] header = new byte[12];
            stream.Read(header, 0, header.Length);
            stream.Position = 0;

            // WAV: RIFF....WAVE
            bool isWav =
                header[0] == 'R' && header[1] == 'I' && header[2] == 'F' && header[3] == 'F' &&
                header[8] == 'W' && header[9] == 'A' && header[10] == 'V' && header[11] == 'E';

            // OGG: "OggS"
            bool isOgg =
                header[0] == 'O' && header[1] == 'g' && header[2] == 'g' && header[3] == 'S';

            // FLAC: "fLaC"
            bool isFlac =
                header[0] == 0x66 && header[1] == 0x4C && header[2] == 0x61 && header[3] == 0x43;

            // MP3: ID3 или frame sync
            bool isMp3 =
                (header[0] == 0x49 && header[1] == 0x44 && header[2] == 0x33) ||  // "ID3"
                (header[0] == 0xFF && (header[1] & 0xE0) == 0xE0);

            return isWav || isOgg || isFlac || isMp3;
        }

        public string ChordsOutput { get; set; }
        public int[] ChordsIntervals { get; set; }
        [HttpPost]
        public async Task<IActionResult> OnPostRecognizeChords()
        {
            if (!string.IsNullOrEmpty(UploadedFileUrl))
            {
                string file = Path.Combine(_env.WebRootPath, UploadedFileUrl);
                bool hasFinished = false;

                var chordProcessingProgress = new Progress<int>(v =>
                {
                    Console.WriteLine($@"Computing chords... {v} %");
                    if (v >= 100)
                    {
                        hasFinished = true;
                    }
                });

                var (sampleRate, samples) = await Task.Run(() => Profiling.GetSamples(file));

                chordsPredicted = await Task.Run(() =>
                    _predictor.GetPredictionWithBorderDetection(samples, sampleRate,
                        500, 100, chordProcessingProgress)
                );

                chordsIntervals = new int[chordsPredicted.Length];
                chordsIntervals[0] = chordsPredicted[0].DurationInMs();
                for (int i = 1; i < chordsIntervals.Length; i++)
                {
                    chordsIntervals[i] = chordsIntervals[i - 1] + chordsPredicted[i].DurationInMs();
                }

                ChordsOutput = string.Join(" ", chordsPredicted.Select(c => c.Name.ToString()));
                ChordsIntervals = chordsIntervals;

                Console.WriteLine(ChordsOutput);
            }

            return Page();
        }
    }
}
