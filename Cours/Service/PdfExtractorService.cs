
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Cours.Service
{
    /// <summary>
    /// Extraction de texte depuis un PDF (PdfPig) et stockage des documents déposés.
    ///
    /// PdfPig est 100% .NET, pas de dépendance native, fonctionne partout.
    /// Il produit du texte brut, sans mise en forme : c'est l'agent Structurateur qui
    /// transforme ensuite ce dump en cours lisible.
    ///
    /// Flux :
    ///   Upload → SaveAssetAsync (disque) → ExtractText (PDF) ou TranscriptionAgent (photos)
    ///   → StructurateurAgent → FormattedMarkdown → chunks + embeddings
    /// </summary>
    public class PdfExtractorService
    {
        /// <summary>
        /// Extrait tout le texte d'un PDF depuis un stream.
        /// Utilisé lors de l'upload : on lit le fichier en mémoire et on extrait.
        /// </summary>
        public string ExtractText(Stream pdfStream)
        {
            using var document = PdfDocument.Open(pdfStream);
            var sb = new System.Text.StringBuilder();

            foreach (Page page in document.GetPages())
            {
                sb.AppendLine(page.Text);
            }

            return sb.ToString().Trim();
        }

        /// <summary>
        /// Extrait le texte depuis un fichier déjà enregistré sur le disque.
        /// Utilisé pour ré-extraire sans re-upload (bouton « Régénérer la mise en forme »).
        /// </summary>
        public string ExtractTextFromPath(string filePath)
        {
            using var stream = File.OpenRead(filePath);
            return ExtractText(stream);
        }

        /// <summary>
        /// Sauvegarde un document déposé et retourne son chemin relatif à wwwroot.
        /// Accepte PDF et images : c'est ce qui permet de déposer les photos d'un cours.
        /// </summary>
        public async Task<string> SaveAssetAsync(IFormFile file, string uploadFolder, CancellationToken ct = default)
        {
            Directory.CreateDirectory(uploadFolder);

            // L'extension vient du nom de fichier fourni par le client : ne jamais la
            // réutiliser telle quelle dans un chemin. On la valide contre une liste fermée.
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!AllowedExtensions.Contains(extension))
                throw new ArgumentException($"Extension non autorisée : {extension}", nameof(file));

            var fileName = $"{Guid.NewGuid()}{extension}";
            var filePath = Path.Combine(uploadFolder, fileName);

            await using var stream = File.Create(filePath);
            await file.CopyToAsync(stream, ct);

            // Chemin relatif (pas absolu) pour la portabilité et pour être servable par
            // UseStaticFiles / le proxy /uploads/cours du frontend.
            return Path.Combine("uploads", "cours", fileName).Replace("\\", "/");
        }

        private static readonly HashSet<string> AllowedExtensions =
            new(StringComparer.OrdinalIgnoreCase) { ".pdf", ".jpg", ".jpeg", ".png", ".webp" };
    }
}
