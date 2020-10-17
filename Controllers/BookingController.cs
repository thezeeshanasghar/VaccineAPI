using System;
using System.Collections.Generic;
using AutoMapper;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using Microsoft.AspNetCore.Mvc;
using VaccineAPI.ModelDTO;
using VaccineAPI.Models;
using System.IO;

namespace VaccineAPI.Controllers
{
    [Route ("api/[controller]")]
    [ApiController]
    public class BookingController : ControllerBase {
        private readonly Context _db;
        private readonly IMapper _mapper;

        static readonly string[] Scopes = { SheetsService.Scope.Spreadsheets }; //SheetsService.Scope.SpreadsheetsReadonly
        static readonly string ApplicationName = "VaccineAPI"; //"quickstart-1599807090946";
        static readonly string SpreadsheetId = "1VxF4JqAPwfZZomaf3GctMkWAq3nEg0N4yTjkCYJr_PY";
        static SheetsService service;

        public BookingController (Context context, IMapper mapper) {
            _db = context;
            _mapper = mapper;
        }

        [HttpPost]
        public Response<BookingDTO> AddBooking (BookingDTO bookingDTO) {
            Init ();
             Console.WriteLine(bookingDTO);
            AddRow (bookingDTO);

            return new Response<BookingDTO> (true, "Booking successfully.", null);

        }

        static void Init () {
            GoogleCredential credential;
            //Reading Credentials File...
            using (var stream = new FileStream ("app_client_secret.json", FileMode.Open, FileAccess.Read)) {
                credential = GoogleCredential.FromStream (stream)
                    .CreateScoped (Scopes);

            }
            // Creating Google Sheets API service...
            service = new SheetsService (new BaseClientService.Initializer () {
                HttpClientInitializer = credential,
                    ApplicationName = ApplicationName,
            });
        }

        static void AddRow (BookingDTO data) {
            // Specifying Column Range for reading...
            var range = "A:K"; //$"{sheet}!A:B";
            var valueRange = new ValueRange ();
            // Data for new row 
            var oblist = new List<object>{data.ChildName , data.FatherName , data.DOB , data.Vaccines , data.Email , data.Phone , data.Address , data.Card , data.City , data.BookingDate, data.Status};//{ "Harry", "80" };
            Console.WriteLine(oblist);
           valueRange.Values = new List<IList<object>>{ oblist };
            // Append the above record...
            var appendRequest = service.Spreadsheets.Values.Append (valueRange, SpreadsheetId, range);
            appendRequest.ValueInputOption = SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.USERENTERED;
            var appendReponse = appendRequest.Execute ();
        }

    }
}

// https://dottutorials.net/google-sheets-read-write-operations-dotnet-core-tutorial/