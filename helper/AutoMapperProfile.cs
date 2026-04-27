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
            CreateMap<Stock, StockDTO>().ReverseMap();
            CreateMap<AdjustStock, AdjustStockDTO>().ReverseMap();
            CreateMap<StockTransfer, StockTransferHistoryDTO>()
                .ForMember(d => d.FromClinicName, o => o.MapFrom(s => s.FromClinic != null ? s.FromClinic.Name : ""))
                .ForMember(d => d.ToClinicName, o => o.MapFrom(s => s.ToClinic != null ? s.ToClinic.Name : ""))
                .ForMember(d => d.BrandName, o => o.MapFrom(s => s.Brand != null ? s.Brand.Name : ""))
                .ForMember(d => d.TransferredByName, o => o.Ignore());
        }
    }
}
