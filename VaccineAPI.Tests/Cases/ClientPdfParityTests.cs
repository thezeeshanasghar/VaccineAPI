using iTextSharp.text.pdf;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using VaccineAPI.Controllers;
using VaccineAPI.Tests.Infrastructure;
using Xunit;

namespace VaccineAPI.Tests.Cases;

/// <summary>
/// The client's download (Download-PDF / Download-Schedule-PDF) must hand a Travel child the same certificate the clinic
/// prints (given-only), under the same name; everything else the clinic prints must be unchanged.
/// Fonts are embedded, so printed text isn't readable here: "same document" is proven by comparing the decoded page
/// content streams. Set PDF_DUMP_DIR to also write each PDF out for text/visual inspection.
/// </summary>
public class ClientPdfParityTests
{
    private sealed class StubHost : Microsoft.AspNetCore.Hosting.IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "VaccineAPI.Tests";
        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
        public string WebRootPath { get; set; } = Path.GetTempPath();
        public string EnvironmentName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    private static ChildController Ctl(VaccineAPI.Models.Context db) =>
        new ChildController(db, null!, new StubHost(), new ConfigurationBuilder().Build())
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };

    // A Travel child: dose 1 given, dose 2 still due. Every printed field is filled so the PDF builder has no nulls to trip on.
    private static StockWorld TravelWorld(string type = "travel")
    {
        var w = new StockWorld();
        using var db = w.NewContext();
        db.BypassInventoryWriterCheck = true;
        var child = db.Childs.First(c => c.Id == w.ChildId);
        child.Type = type; child.Name = "Hiba Shahid"; child.FatherName = "Col Shahid"; child.CNIC = "XD5152501";
        child.City = "Rawalpindi"; child.Nationality = "Pakistani"; child.Email = "";
        var doctor = db.Doctors.First(d => d.Id == w.DoctorId);
        doctor.AdditionalInfo = "MBBS";
        db.Clinics.First(c => c.Id == w.ClinicA).RegNo = "REG-1";
        db.Doses.First(d => d.Id == w.Dose1Id).Name = "MenACWY 1";
        db.Doses.First(d => d.Id == w.Dose2Id).Name = "Typhoid 1";
        var s1 = db.Schedules.First(s => s.Id == w.Schedule1Id);
        s1.IsDone = true; s1.GivenDate = StockWorld.Today; s1.BrandId = w.BrandId;
        db.SaveChanges();
        return w;
    }

    private static void Set(StockWorld w, long scheduleId, bool done, DateTime? given)
    {
        using var db = w.NewContext();
        db.BypassInventoryWriterCheck = true;
        var s = db.Schedules.First(x => x.Id == scheduleId);
        s.IsDone = done; s.GivenDate = given;
        db.SaveChanges();
    }

    private static byte[] Content(byte[] pdf)
    {
        var r = new PdfReader(pdf);
        using var ms = new MemoryStream();
        for (int p = 1; p <= r.NumberOfPages; p++) { var c = r.GetPageContent(p); ms.Write(c, 0, c.Length); }
        return ms.ToArray();
    }

    private static FileContentResult Pdf(IActionResult r, string? dump = null)
    {
        var f = Assert.IsType<FileContentResult>(r);
        Assert.Equal("application/pdf", f.ContentType);
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(f.FileContents, 0, 4));
        var dir = Environment.GetEnvironmentVariable("PDF_DUMP_DIR");
        if (dump != null && !string.IsNullOrEmpty(dir)) File.WriteAllBytes(Path.Combine(dir, dump + ".pdf"), f.FileContents);
        return f;
    }

    [Fact]
    public void TravelClient_GetsACertificate_WithTheDoctorsGivenOnlyFileName()
    {
        using var w = TravelWorld();
        using var db = w.NewContext();
        var f = Pdf(Ctl(db).DownloadClientPdf((int)w.ChildId), "client_travel");
        Assert.Matches(@"^Hiba_Shahid_Travel_Immunization_Given_[A-Za-z]+-\d{2}-\d{4}\.pdf$", f.FileDownloadName);
    }

    [Fact]
    public void ClientCopy_IsTheSameDocument_AsTheDoctorsGivenOnlyDownload()
    {
        using var w = TravelWorld();
        using var db1 = w.NewContext();
        var client = Pdf(Ctl(db1).DownloadClientPdf((int)w.ChildId));

        using var db2 = w.NewContext();
        var doctorCtl = Ctl(db2);
        var doctor = Pdf(doctorCtl.GenerateTravelPdf((int)w.ChildId, includeFuture: false), "doctor_given_only");
        string disp = doctorCtl.Response.Headers["Content-Disposition"].ToString();

        Assert.Contains(client.FileDownloadName, disp);                 // identical file name
        Assert.Equal(Content(doctor.FileContents), Content(client.FileContents));   // identical page content
    }

    [Fact]
    public void DoctorAllVaccinesDownload_IsUnchanged_DiffersFromGivenOnly_AndHasNoGivenSuffix()
    {
        using var w = TravelWorld();
        using var db1 = w.NewContext();
        var all = Ctl(db1);
        var allPdf = Pdf(all.GenerateTravelPdf((int)w.ChildId), "doctor_all");          // includeFuture defaults to true
        using var db2 = w.NewContext();
        var given = Pdf(Ctl(db2).GenerateTravelPdf((int)w.ChildId, false));

        Assert.NotEqual(Content(given.FileContents), Content(allPdf.FileContents));    // the due dose is only on the full copy
        string disp = all.Response.Headers["Content-Disposition"].ToString();
        Assert.Matches(@"Hiba_Shahid_Travel_Immunization_[A-Za-z]+-\d{2}-\d{4}\.pdf", disp);
        Assert.DoesNotContain("Given_", disp);
    }

    [Theory]
    [InlineData("Travel")]
    [InlineData("TRAVEL")]
    [InlineData("travel")]
    public void TravelType_IsMatchedCaseInsensitively(string type)
    {
        using var w = TravelWorld(type);
        using var db = w.NewContext();
        var f = Pdf(Ctl(db).DownloadClientPdf((int)w.ChildId));
        Assert.Contains("_Travel_Immunization_Given_", f.FileDownloadName);
    }

    [Fact]
    public void GivenOnly_DropsADoneDoseWithNoGivenDate_SameAsADoseThatIsStillDue()
    {
        using var due = TravelWorld();
        using var broken = TravelWorld();
        Set(broken, broken.Schedule2Id, done: true, given: null);   // done, but nothing to certify

        using var d1 = due.NewContext(); using var d2 = broken.NewContext();
        var a = Pdf(Ctl(d1).DownloadClientPdf((int)due.ChildId));
        var b = Pdf(Ctl(d2).DownloadClientPdf((int)broken.ChildId), "client_done_no_date");
        Assert.Equal(Content(a.FileContents), Content(b.FileContents));
    }

    [Fact]
    public void GivenOnly_WithNothingGiven_StillProducesACertificate_DifferentFromOneWithADose()
    {
        using var none = TravelWorld();
        Set(none, none.Schedule1Id, done: false, given: null);
        using var one = TravelWorld();

        using var d1 = none.NewContext(); using var d2 = one.NewContext();
        var empty = Pdf(Ctl(d1).DownloadClientPdf((int)none.ChildId), "client_empty");
        var full = Pdf(Ctl(d2).DownloadClientPdf((int)one.ChildId));
        Assert.NotEqual(Content(empty.FileContents), Content(full.FileContents));
    }

    [Fact]
    public void NonTravelChild_StillGetsTheScheduleCard_UnderTheOldName()
    {
        using var w = TravelWorld("");
        using var db = w.NewContext();
        var r = Ctl(db).DownloadClientPdf((int)w.ChildId);
        var name = r switch
        {
            FileContentResult f => f.FileDownloadName,
            FileStreamResult s => s.FileDownloadName,
            _ => throw new Xunit.Sdk.XunitException("expected a file result, got " + r.GetType().Name)
        };
        Assert.Matches(@"^HibaShahid_Schedule_[A-Za-z]+-\d{2}-\d{4}\.pdf$", name);
    }

    [Fact]
    public void NonTravelClientCard_IsTheSameDocument_AsWhatTheDoctorGetsFromScheduleVerify()
    {
        using var w = TravelWorld("");
        static byte[] Bytes(IActionResult r)
        {
            if (r is FileContentResult f) return f.FileContents;
            var s = Assert.IsType<FileStreamResult>(r);
            using var ms = new MemoryStream(); s.FileStream.CopyTo(ms); return ms.ToArray();
        }
        using var db1 = w.NewContext(); using var db2 = w.NewContext();
        var client = Bytes(Ctl(db1).DownloadClientPdf((int)w.ChildId));
        var doctor = Bytes(Ctl(db2).DownloadSchedulePDF((int)w.ChildId));
        Assert.Equal(Content(doctor), Content(client));
    }

    [Fact]
    public void LegacyDownloadSchedulePdf_GivesTravelChildrenTheCertificate_AndOthersTheOldHtmlWithTheNewMr()
    {
        using (var w = TravelWorld())
        {
            using var db = w.NewContext();
            var f = Pdf(Ctl(db).GenerateVerifySchedule((int)w.ChildId));
            Assert.Contains("_Travel_Immunization_Given_", f.FileDownloadName);
        }
        using (var w = TravelWorld(""))
        {
            using var db = w.NewContext();
            var c = Assert.IsType<ContentResult>(Ctl(db).GenerateVerifySchedule((int)w.ChildId));
            Assert.Equal("text/html", c.ContentType);
            Assert.Contains($"{Pkt.NowWall().Year}-{w.ChildId}", c.Content);
        }
    }

    [Fact]
    public void VerifyRecord_StillResolves_TheNewAndEveryOlderMrFormat()
    {
        using var w = TravelWorld();
        const int realisticId = 19116;                      // real ids are 5 digits; "yy"+"1" would be ambiguous
        using (var db = w.NewContext())
        {
            db.BypassInventoryWriterCheck = true;
            var src = db.Childs.AsNoTracking().First(c => c.Id == w.ChildId);
            src.Id = realisticId;
            db.Childs.Add(src);
            db.SaveChanges();
        }
        int yy = Pkt.NowWall().Year % 100;
        foreach (var mr in new[] { $"{Pkt.NowWall().Year}-{realisticId}", $"{realisticId}", $"{yy}{realisticId}", $"{yy}-{realisticId}" })
        {
            using var db = w.NewContext();
            Assert.True(Ctl(db).VerifyRecord(mr) is OkObjectResult, "MR format not resolved: " + mr);
        }
    }

    [Fact]
    public void ClientRoute_IsOwnerChecked_AndIsNotOneOfThePublicVerifyRoutes()
    {
        var m = typeof(ChildController).GetMethod(nameof(ChildController.DownloadClientPdf))!;
        Assert.NotEmpty(m.GetCustomAttributes(typeof(OwnsAttribute), false));
        Assert.False(PublicEndpoints.IsPublic("GET", "/api/Child/5/Download-PDF"));
        Assert.False(PublicEndpoints.IsPublic("GET", "/api/Child/5/Download-Schedule-PDF"));
        // the routes officers/QR scans use must stay public
        Assert.True(PublicEndpoints.IsPublic("GET", "/api/Child/5/ScheduleVerify"));
        Assert.True(PublicEndpoints.IsPublic("GET", "/api/Child/Travel-PDF-Download-Verification/5"));
    }
}
