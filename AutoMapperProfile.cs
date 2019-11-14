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
            CreateMap<Vaccine, UserDTO>().ReverseMap();
            CreateMap<Vaccine, ScheduleDTO>().ReverseMap();
            CreateMap<Vaccine, ScheduleBrandDTO>().ReverseMap();
            CreateMap<Vaccine, MessageDTO>().ReverseMap();
            CreateMap<Vaccine, FollowUpDTO>().ReverseMap();
            CreateMap<Vaccine, DoseDTO>().ReverseMap();
            CreateMap<Vaccine, DoctorScheduleDTO>().ReverseMap();
            CreateMap<Vaccine, DoctorDTO>().ReverseMap();
            CreateMap<Vaccine, ClinicTimingDTO>().ReverseMap();
            CreateMap<Vaccine, ClinicDTO>().ReverseMap();
            CreateMap<Vaccine, ChildDTO>().ReverseMap();
            CreateMap<Vaccine, ChangePasswordRequestDTO>().ReverseMap();
            CreateMap<Vaccine, BrandInventoryDTO>().ReverseMap();
            CreateMap<Vaccine, BrandDTO>().ReverseMap();
            CreateMap<Vaccine, BrandAmountDTO>().ReverseMap();
        }

    }
}
