using System.Collections.Generic;

namespace OpenSubtitlesHandler.Models.Responses
{
    public class SearchResult
    {
        public int TotalPages;
        public int TotalCount;
        public int Page;
        public List<Data> Data;
    }
}
