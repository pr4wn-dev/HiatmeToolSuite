namespace Hiatme_Tool_Suite_v3
{
    internal static class TemplateTripRowParser
    {
        public static MCDownloadedTrip FromRow(string[] row)
        {
            string Cell(int i) => i < row.Length ? (row[i] ?? "").Trim() : "";
            return new MCDownloadedTrip
            {
                TripNumber = Cell(0),
                Date = Cell(1),
                ClientFullName = Cell(2),
                PUStreet = Cell(3),
                PUCity = Cell(4),
                PUTelephone = Cell(5),
                PUTime = TripTemplateCsvValidator.NormalizeTimeField(Cell(6)),
                DOStreet = Cell(7),
                DOCITY = Cell(8),
                DOTelephone = Cell(9),
                DOTime = TripTemplateCsvValidator.NormalizeTimeField(Cell(10)),
                Age = Cell(11),
                Miles = Cell(12),
                Comments = Cell(13),
            };
        }
    }
}
