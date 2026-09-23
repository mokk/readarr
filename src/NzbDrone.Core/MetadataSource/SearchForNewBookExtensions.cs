using System.Linq;
using NzbDrone.Core.Books;

namespace NzbDrone.Core.MetadataSource
{
    public static class SearchForNewBookExtensions
    {
        /// <summary>
        /// Fetch a single edition directly by its foreign (Goodreads) edition id.
        /// Used when an edition is not part of the (trimmed) edition list returned for its work.
        /// Returns null if the id is not numeric or the edition cannot be found.
        /// </summary>
        public static Edition GetEditionByForeignEditionId(this ISearchForNewBook searchForNewBook, string foreignEditionId)
        {
            if (!int.TryParse(foreignEditionId, out var id))
            {
                return null;
            }

            var book = searchForNewBook.SearchByGoodreadsBookId(id, false).FirstOrDefault();

            return book?.Editions?.Value?.FirstOrDefault(x => x.ForeignEditionId == foreignEditionId);
        }
    }
}
