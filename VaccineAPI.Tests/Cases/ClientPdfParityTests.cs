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
public class ClientPdfParityTests : IDisposable
{
    private readonly IHttpContextAccessor? _savedAccessor = AuthContext.Accessor;
    public void Dispose() => AuthContext.Accessor = _savedAccessor;

    // The client routes require a signed-in owner (a token), whatever Auth.Mode is, so every test acts as someone.
    private static void Login(AuthIdentity? id)
    {
        var http = new DefaultHttpContext();
        if (id != null) http.Items["auth.identity"] = id;
        AuthContext.Accessor = new HttpContextAccessor { HttpContext = http };
    }

    private static byte[] Bytes(IActionResult r)
    {
        if (r is FileContentResult f) return f.FileContents;
        var s = Assert.IsType<FileStreamResult>(r);
        using var ms = new MemoryStream(); s.FileStream.CopyTo(ms); return ms.ToArray();
    }

    private static string NameOf(IActionResult r) => r switch
    {
        FileContentResult f => f.FileDownloadName,
        FileStreamResult s => s.FileDownloadName,
        _ => throw new Xunit.Sdk.XunitException("expected a file result, got " + r.GetType().Name)
    };

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
        Login(new AuthIdentity { UserId = child.UserId, Role = "PARENT" });   // the child's own parent
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
    public void ClientWithNothingGiven_GetsNoCertificate_ButTheDoctorsOwnDownloadStillWorks()
    {
        using var none = TravelWorld();
        Set(none, none.Schedule1Id, done: false, given: null);
        using var one = TravelWorld();

        using var d1 = none.NewContext();
        var r = Assert.IsType<ObjectResult>(Ctl(d1).DownloadClientPdf((int)none.ChildId));
        Assert.Equal(409, r.StatusCode);
        Assert.Contains("NO_GIVEN_DOSE", System.Text.Json.JsonSerializer.Serialize(r.Value));

        // the doctor's given-only download is not gated and still prints the empty-state certificate
        using var d2 = none.NewContext(); using var d3 = one.NewContext();
        var doctorEmpty = Pdf(Ctl(d2).GenerateTravelPdf((int)none.ChildId, false), "doctor_empty");
        var doctorOne = Pdf(Ctl(d3).GenerateTravelPdf((int)one.ChildId, false));
        Assert.NotEqual(Content(doctorEmpty.FileContents), Content(doctorOne.FileContents));
    }

    [Fact]
    public void DoneDoseWithNoGivenDate_DoesNotCountAsGiven()
    {
        using var w = TravelWorld();
        Set(w, w.Schedule1Id, done: true, given: null);
        using var db = w.NewContext();
        Assert.Equal(409, Assert.IsType<ObjectResult>(Ctl(db).DownloadClientPdf((int)w.ChildId)).StatusCode);
    }

    [Fact]
    public void Checkformissed_DueTodayIsNotMissed_YesterdayIs()
    {
        using var w = TravelWorld();
        using var db = w.NewContext();
        var c = Ctl(db);
        var today = Pkt.NowWall().Date;
        Assert.False(c.checkForMissed(today));
        Assert.False(c.checkForMissed(today.AddDays(1)));
        Assert.True(c.checkForMissed(today.AddDays(-1)));
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
        // Travel child with nothing given: old apps keep the HTML summary instead of an error file
        using (var w = TravelWorld())
        {
            Set(w, w.Schedule1Id, done: false, given: null);
            using var db = w.NewContext();
            Assert.IsType<ContentResult>(Ctl(db).GenerateVerifySchedule((int)w.ChildId));
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
    public void Anonymous_GetsNothing_AndAStrangerGetsTheSameAnswerAsAMissingChild()
    {
        using var w = TravelWorld();
        Login(null);                                              // Log mode serves token-less requests; this route must not
        using (var db = w.NewContext())
            Assert.Equal(401, Assert.IsType<ObjectResult>(Ctl(db).DownloadClientPdf((int)w.ChildId)).StatusCode);

        Login(new AuthIdentity { UserId = 999, Role = "PARENT" });   // someone else's parent account
        using var db2 = w.NewContext();
        var stranger = Assert.IsType<NotFoundObjectResult>(Ctl(db2).DownloadClientPdf((int)w.ChildId));
        var missing = Assert.IsType<NotFoundObjectResult>(Ctl(db2).DownloadClientPdf(123456));
        Assert.Equal(missing.Value, stranger.Value);              // no way to tell "not yours" from "does not exist"
    }

    [Fact]
    public void ADoctorOfTheClinicIsAllowed_ADoctorOfAnotherPracticeIsNot()
    {
        using var w = TravelWorld();
        Login(new AuthIdentity { UserId = 1, Role = "DOCTOR", DoctorId = w.DoctorId });
        using (var db = w.NewContext()) Pdf(Ctl(db).DownloadClientPdf((int)w.ChildId));
        Login(new AuthIdentity { UserId = 50, Role = "DOCTOR", DoctorId = w.DoctorId + 77 });
        using var db2 = w.NewContext();
        Assert.IsType<NotFoundObjectResult>(Ctl(db2).DownloadClientPdf((int)w.ChildId));
    }

    [Fact]
    public void LegacyRoute_NeverHandsTheCertificateToAnAnonymousCaller()
    {
        using var w = TravelWorld();
        Login(null);
        using var db = w.NewContext();
        Assert.IsType<ContentResult>(Ctl(db).GenerateVerifySchedule((int)w.ChildId));   // the old summary page, as before
    }

    [Fact]
    public void CustomClient_GetsTheSameDocumentsAsTheDoctor_AllVaccinesAndGivenOnly_PortraitAndLandscape()
    {
        using var w = TravelWorld("special");
        foreach (var includeFuture in new[] { true, false })
        {
            using var a = w.NewContext(); using var b = w.NewContext(); using var c = w.NewContext(); using var d = w.NewContext();
            var clientP = Ctl(a).DownloadClientPdf((int)w.ChildId, "portrait", includeFuture);
            var doctorP = Ctl(b).DownloadCustomPDF((int)w.ChildId, includeFuture);
            Assert.Equal(NameOf(doctorP), NameOf(clientP));
            Assert.Equal(Content(Bytes(doctorP)), Content(Bytes(clientP)));

            var clientL = Ctl(c).DownloadClientPdf((int)w.ChildId, "landscape", includeFuture);
            var doctorL = Ctl(d).DownloadCustomLandscapePDF((int)w.ChildId, includeFuture);
            Assert.Equal(NameOf(doctorL), NameOf(clientL));
            Assert.Equal(Content(Bytes(doctorL)), Content(Bytes(clientL)));
        }
    }

    [Fact]
    public void CustomClient_Rules_GivenOnlyNeedsAGivenDose_UnknownLayoutIs400_NoLayoutKeepsTheCard()
    {
        using var w = TravelWorld("special");
        Set(w, w.Schedule1Id, done: false, given: null);
        using var db = w.NewContext();
        var c = Ctl(db);
        Assert.Equal(409, Assert.IsType<ObjectResult>(c.DownloadClientPdf((int)w.ChildId, "portrait", false)).StatusCode);
        Assert.Equal(409, Assert.IsType<ObjectResult>(c.DownloadClientPdf((int)w.ChildId, "landscape", false)).StatusCode);
        Assert.Contains("_Schedule_", NameOf(c.DownloadClientPdf((int)w.ChildId, "landscape", true)));   // all vaccines needs no dose
        Assert.IsType<BadRequestObjectResult>(c.DownloadClientPdf((int)w.ChildId, "sideways", true));
        Assert.DoesNotContain("_Given_", NameOf(c.DownloadClientPdf((int)w.ChildId)));                  // no layout = unchanged schedule card
    }

    [Theory]
    [InlineData("MenACWY 1", "MenACWY")]
    [InlineData("Hep B 3", "Hep B")]
    [InlineData("Typhoid 12", "Typhoid")]
    [InlineData("Flu 2025", "Flu 2025")]       // a year is not a dose number
    [InlineData("MenACWY", "MenACWY")]
    [InlineData("", "")]
    public void CertificateDoseName_DropsOnlyATrailingDoseNumber(string input, string expected) =>
        Assert.Equal(expected, ChildController.CertificateDoseName(input));

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
