using VaccineAPI.Controllers;
using Xunit;

namespace VaccineAPI.Tests.Cases;

/// <summary>Which push recipient a logged-in role's device belongs to. The owner comes from the session only.</summary>
public class DeviceOwnerTests
{
    private static AuthIdentity Id(string role, long userId = 5, long? doctorId = null, long? paId = null)
        => new AuthIdentity { Role = role, UserId = userId, DoctorId = doctorId, PaId = paId };

    [Fact] public void Parent_is_keyed_by_user_id() => Assert.Equal(("PARENT", 5L), DeviceController.Owner(Id("PARENT"))!.Value);
    [Fact] public void Agent_is_keyed_by_agent_id() => Assert.Equal(("AGENT", 5L), DeviceController.Owner(Id("AGENT"))!.Value);
    [Fact] public void Doctor_is_keyed_by_doctor_id() => Assert.Equal(("DOCTOR", 9L), DeviceController.Owner(Id("DOCTOR", doctorId: 9))!.Value);
    [Fact] public void Pa_is_keyed_by_pa_id() => Assert.Equal(("PA", 12L), DeviceController.Owner(Id("PA", paId: 12))!.Value);

    [Fact] // a manager receives their doctor's pushes
    public void Manager_is_keyed_as_the_doctor() => Assert.Equal(("DOCTOR", 9L), DeviceController.Owner(Id("MANAGER", doctorId: 9))!.Value);

    [Fact] public void No_session_has_no_owner() => Assert.Null(DeviceController.Owner(null));
    [Fact] public void Doctor_without_a_doctor_row_has_no_owner() => Assert.Null(DeviceController.Owner(Id("DOCTOR")));
    [Fact] public void Pa_without_a_pa_row_has_no_owner() => Assert.Null(DeviceController.Owner(Id("PA")));
    [Fact] public void Superadmin_has_no_owner() => Assert.Null(DeviceController.Owner(Id("SUPERADMIN", doctorId: 1)));
}
