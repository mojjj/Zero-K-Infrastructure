using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Web.Mvc;
using PlasmaShared;
using ZkData;

namespace ZeroKWeb.Controllers
{
    /// <summary>
    /// The half of <see cref="PlanetwarsController"/> that draws the galaxy map, split off so
    /// the rest of the controller can be linked into the .NET 9 port.
    ///
    /// Everything here is System.Drawing: Bitmap, Graphics, Image.FromFile. Those types throw
    /// PlatformNotSupportedException off Windows since .NET 7 and live in a Windows-only
    /// package, so they cannot cross - see Shared/PlasmaShared/IMAGING-MIGRATION.md, which
    /// tracks the ImageSharp replacement.
    ///
    /// This is the same move that let PlanetwarsAdminController be linked: split the seam, link
    /// the portable half, and let the compiler hold the line. The port has no Index action for
    /// this controller as a result; Galaxy.cshtml takes a ZkData.Galaxy, not a controller type,
    /// so the VIEW is unaffected.
    ///
    /// When GenerateGalaxyImage is ported to Images.Processor this file and the `partial`
    /// keyword next door both go away.
    /// </summary>
    public partial class PlanetwarsController
    {
        /// <summary>
        /// Makes an image: galaxy background with planet images drawn on it (cheaper than rendering each planet individually)
        /// </summary>
        // FIXME: having issues with bitmap parameters; setting AA factor to 1 as fallback (was 4)
        public Bitmap GenerateGalaxyImage(int galaxyID, double zoom = 1, double antiAliasingFactor = 1)
        {
            zoom *= antiAliasingFactor;
            using (var db = new ZkDataContext())
            {
                Galaxy gal = db.Galaxies.Single(x => x.GalaxyID == galaxyID);

                using (Image background = Image.FromFile(Server.MapPath("/img/galaxies/" + gal.ImageName)))
                {
                    //var im = new Bitmap((int)(background.Width*zoom), (int)(background.Height*zoom));
                    var im = new Bitmap(background.Width, background.Height);
                    using (Graphics gr = Graphics.FromImage(im))
                    {
                        gr.DrawImage(background, 0, 0, im.Width, im.Height);

                        /*
						using (var pen = new Pen(Color.FromArgb(255, 180, 180, 180), (int)(1*zoom)))
						{
							foreach (var l in gal.Links)
							{
								gr.DrawLine(pen,
								            (int)(l.PlanetByPlanetID1.X*im.Width),
								            (int)(l.PlanetByPlanetID1.Y*im.Height),
								            (int)(l.PlanetByPlanetID2.X*im.Width),
								            (int)(l.PlanetByPlanetID2.Y*im.Height));
							}
						}*/

                        foreach (Planet p in gal.Planets)
                        {
                            string planetIconPath = null;
                            try
                            {
                                planetIconPath = "/img/planets/" + (p.Resource.MapPlanetWarsIcon ?? "1.png"); // backup image is 1.png
                                using (Image pi = Image.FromFile(Server.MapPath(planetIconPath)))
                                {
                                    double aspect = pi.Height / (double)pi.Width;
                                    var width = (int)(p.Resource.PlanetWarsIconSize * zoom);
                                    var height = (int)(width * aspect);
                                    gr.DrawImage(pi, (int)(p.X * im.Width) - width / 2, (int)(p.Y * im.Height) - height / 2, width, height);
                                }
                            }
                            catch (Exception ex)
                            {
                                throw new ApplicationException(
                                    string.Format("Cannot process planet image {0} for planet {1} map {2}",
                                                  planetIconPath,
                                                  p.PlanetID,
                                                  p.MapResourceID),
                                    ex);
                            }
                        }
                        if (antiAliasingFactor == 1) return im;
                        else
                        {
                            zoom /= antiAliasingFactor;
                            return im.GetResized((int)(background.Width * zoom), (int)(background.Height * zoom), InterpolationMode.HighQualityBicubic);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Go to main Planetwars page
        /// </summary>
        public ActionResult Index(int? galaxyID = null)
        {
            var db = new ZkDataContext();
            
            Galaxy gal;
            if (galaxyID != null) gal = db.Galaxies.Single(x => x.GalaxyID == galaxyID);
            else gal = db.Galaxies.Single(x => x.IsDefault);

            string cachePath = Server.MapPath(string.Format("/img/galaxies/render_{0}.jpg", gal.GalaxyID));
            if (gal.IsDirty || !System.IO.File.Exists(cachePath))
            {
                using (Bitmap im = GenerateGalaxyImage(gal.GalaxyID))
                {
                    im.SaveJpeg(cachePath, 85);
                    gal.IsDirty = false;
                    gal.Width = im.Width;
                    gal.Height = im.Height;
                    db.SaveChanges();
                }
            }
            
            return View("Galaxy", gal);
        }
    }
}
