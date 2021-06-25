using System;
using System.Collections.Generic;

namespace RESTOpenSubtitlesHandler.Models.Responses
{
    public class Attributes
    {
        public string SubtitleId;
        public string Language;
        public int DownloadCount;
        public int NewDownloadCount;
        public bool HearingImpaired;
        public bool Hd;
        public string Format;
        public double Fps;
        public int Votes;
        public int Points;
        public float Ratings;
        public bool? FromTrusted;
        public bool ForeignPartsOnly;
        public bool AutoTranslation;
        public bool AiTranslated;
        public object MachineTranslated;
        public DateTime UploadDate;
        public string Release;
        public string Comments;
        public int? LegacySubtitleId;
        public Uploader Uploader;
        public FeatureDetails FeatureDetails;
        public string Url;
        public List<SubFile> Files;
        public bool? MoviehashMatch;
    }
}
