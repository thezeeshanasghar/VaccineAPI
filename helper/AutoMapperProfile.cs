using AutoMapper;
using VaccineAPI.ModelDTO;
using VaccineAPI.Models;

namespace VaccineAPI
{
    public class AutoMapperProfile : Profile
    {
        public AutoMapperProfile()
        {
            CreateMap<Vaccine, VaccineDTO>().ReverseMap();
            CreateMap<User, UserDTO>().ReverseMap();
            CreateMap<Schedule, ScheduleDTO>().ReverseMap();
            CreateMap<Message, MessageDTO>().ReverseMap();
            CreateMap<FollowUp, FollowUpDTO>().ReverseMap();
            CreateMap<Dose, DoseDTO>().ReverseMap();
            CreateMap<DoctorSchedule, DoctorScheduleDTO>().ReverseMap();
            CreateMap<Doctor, DoctorDTO>().ReverseMap();
            CreateMap<ClinicTiming, ClinicTimingDTO>().ReverseMap();
            CreateMap<Clinic, ClinicDTO>().ReverseMap();
            CreateMap<Child, ChildDTO>().ReverseMap();
            // CreateMap<BrandInventory, BrandInventoryDTO>().ReverseMap();
            CreateMap<Brand, BrandDTO>().ReverseMap();
            CreateMap<BrandAmount, BrandAmountDTO>().ReverseMap();
            CreateMap<Bill, BillDTO>().ReverseMap();
            CreateMap<Supplier, SupplierDTO>().ReverseMap();
            CreateMap<SupplierPayment, SupplierPaymentDTO>().ReverseMap();
            CreateMap<Stock, StockDTO>().ReverseMap();
            CreateMap<AdjustStock, AdjustStockDTO>().ReverseMap();
            CreateMap<DirectSale, DirectSaleDTO>()
                .ForMember(d => d.BrandName,  o => o.MapFrom(s => s.Brand != null  ? s.Brand.Name  : ""))
                .ForMember(d => d.ClinicName, o => o.MapFrom(s => s.Clinic != null ? s.Clinic.Name : ""));
            CreateMap<ExpenseCategory, ExpenseCategoryDTO>().ReverseMap();
            CreateMap<Expense, ExpenseDTO>()
                .ForMember(d => d.ClinicName,   o => o.MapFrom(s => s.Clinic   != null ? s.Clinic.Name   : ""))
                .ForMember(d => d.CategoryName, o => o.MapFrom(s => s.Category != null ? s.Category.Name : ""));
            CreateMap<ExpenseDTO, Expense>()
                .ForMember(d => d.Clinic,   o => o.Ignore())
                .ForMember(d => d.Category, o => o.Ignore())
                .ForMember(d => d.Doctor,   o => o.Ignore());
            CreateMap<StockTransfer, StockTransferHistoryDTO>()
                .ForMember(d => d.FromClinicName, o => o.MapFrom(s => s.FromClinic != null ? s.FromClinic.Name : ""))
                .ForMember(d => d.ToClinicName, o => o.MapFrom(s => s.ToClinic != null ? s.ToClinic.Name : ""))
                .ForMember(d => d.BrandName, o => o.MapFrom(s => s.Brand != null ? s.Brand.Name : ""))
                .ForMember(d => d.TransferredByName, o => o.Ignore());
        }
    }
}
