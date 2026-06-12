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
            CreateMap<Booking, BookingDTO>().ReverseMap();
            CreateMap<Notification, NotificationDTO>().ReverseMap();
            CreateMap<HomeServiceCity, HomeServiceCityDTO>().ReverseMap();
            CreateMap<FollowUp, FollowUpDTO>().ReverseMap();
            CreateMap<Dose, DoseDTO>().ReverseMap();
            CreateMap<DoctorSchedule, DoctorScheduleDTO>().ReverseMap();
            CreateMap<Doctor, DoctorDTO>().ReverseMap();
            CreateMap<ClinicTiming, ClinicTimingDTO>().ReverseMap();
            CreateMap<Clinic, ClinicDTO>().ReverseMap();
            CreateMap<Child, ChildDTO>()
                .ForMember(dest => dest.Schedules, opt => opt.Ignore())
                .ReverseMap()
                .ForMember(dest => dest.Clinic, opt => opt.Ignore())
                .ForMember(dest => dest.User, opt => opt.Ignore())
                .ForMember(dest => dest.Schedules, opt => opt.Ignore());
            CreateMap<Brand, BrandDTO>().ReverseMap();
        }
    }
}
