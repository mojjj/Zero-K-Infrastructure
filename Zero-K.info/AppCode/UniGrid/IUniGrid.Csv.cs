using System.Text;

namespace ZeroKWeb
{
    /// <summary>
    /// The CSV half of IUniGrid, split off with its implementation - see UniGrid.Csv.cs. A
    /// partial interface, so the portable half does not demand a member the port cannot supply.
    /// </summary>
    public partial interface IUniGrid
    {
        void RenderCsv(Encoding encoding = null, string delimiter = ";");
    }
}
