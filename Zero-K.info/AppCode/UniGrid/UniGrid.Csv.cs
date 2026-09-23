using System.Text;
using System.Web;

namespace ZeroKWeb
{
    /// <summary>
    /// The half of UniGrid that writes a CSV straight onto the response, split off so the rest
    /// can be linked into the .NET 9 port.
    ///
    /// It is System.Web's response MODEL, not just its types: Clear, ClearHeaders, AddHeader,
    /// BinaryWrite and Response.End() - which aborts the request from inside a view. ASP.NET
    /// Core has no equivalent to End(), and the shape a port would use is a FileResult returned
    /// by an action rather than a side effect performed by a grid.
    ///
    /// So this is a design question rather than a translation, and it is left where it works.
    /// GenerateCsv, which builds the text, stayed in the portable half - only the delivery is
    /// here. The same split as PlanetwarsController.Imaging.cs.
    /// </summary>
    public partial class UniGrid<T>
    {
        /// <summary>
        /// Generate CSV and send to browser
        /// </summary>
        /// <param name="encoding">warning default encoding windows-1250</param>
        /// <param name="delimiter"></param>
        public void RenderCsv(Encoding encoding = null, string delimiter = ";") {
            if (!AllowCsvExport) return;
            if (encoding == null) encoding = Encoding.GetEncoding("windows-1250");
            var csv = GenerateCsv(delimiter);

            HttpResponse response = HttpContext.Current.Response;
            response.Clear();
            response.ClearContent();
            response.ClearHeaders();
            response.ContentType = "text/csv";
            var name = CsvFileName;
            if (string.IsNullOrEmpty(name)) name = Title;
            if (string.IsNullOrEmpty(name)) name = "export";
            response.AddHeader("Content-Disposition", string.Format("attachment;filename={0}.csv", name));
            response.BinaryWrite(encoding.GetBytes(csv));
            response.End();
        }
    }
}
