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
            CreateMap<BrandInventory, BrandInventoryDTO>().ReverseMap();
            CreateMap<Brand, BrandDTO>().ReverseMap();
            CreateMap<BrandAmount, BrandAmountDTO>().ReverseMap();
        }
    }
}
