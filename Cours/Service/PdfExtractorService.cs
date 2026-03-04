
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Cours.Service
{


    /// <summary>
    /// Service d'extraction de texte depuis un PDF via la librairie PdfPig.
    ///
    /// PdfPig est 100% .NET, pas de dépendance native, fonctionne partout.
    /// C'est lui qui transforme un PDF en texte brut que le LLM peut lire.
    ///
    /// Flux :
    ///   Client uploade PDF → PdfExtractorService extrait le texte
    ///   → ExtractedText stocké en DB → Chat service envoie au LLM
    /// </summary>
    /// 
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
        /// Utilisé si on a besoin de ré-extraire sans re-upload.
        /// </summary>
        public string ExtractTextFromPath(string filePath)
        {
            using var stream = File.OpenRead(filePath);
            return ExtractText(stream);
        }

        /// <summary>
        /// Sauvegarde le PDF sur le disque et retourne le chemin.
        /// Les fichiers sont stockés dans wwwroot/uploads/cours/.
        /// </summary>
        public async Task<string> SavePdfAsync(IFormFile file, string uploadFolder)
        {
            Directory.CreateDirectory(uploadFolder);

            var fileName = $"{Guid.NewGuid()}.pdf";
            var filePath = Path.Combine(uploadFolder, fileName);

            using var stream = File.Create(filePath);
            await file.CopyToAsync(stream);

            // Retourner le chemin relatif (pas absolu) pour la portabilité
            return Path.Combine("uploads", "cours", fileName).Replace("\\", "/");
        }
    
}
}
