using System.Text.Json.Serialization;

namespace DrSharonKellyEnt.Forms;

public class ContactFormRequest
{
    [JsonPropertyName("firstName")] public string FirstName { get; set; } = "";
    [JsonPropertyName("lastName")] public string LastName { get; set; } = "";
    [JsonPropertyName("email")] public string Email { get; set; } = "";
    [JsonPropertyName("phone")] public string Phone { get; set; } = "";
    [JsonPropertyName("enquirerType")] public string EnquirerType { get; set; } = "";
    [JsonPropertyName("preferredLocation")] public string PreferredLocation { get; set; } = "";
    [JsonPropertyName("message")] public string Message { get; set; } = "";
    [JsonPropertyName("recaptchaToken")] public string? RecaptchaToken { get; set; }
    [JsonPropertyName("website")] public string? Website { get; set; } // honeypot — must stay empty
    [JsonPropertyName("formLoadedAt")] public long FormLoadedAt { get; set; } // client Date.now() at page load
}

public class ReferralFormRequest
{
    [JsonPropertyName("gpFirstName")] public string GpFirstName { get; set; } = "";
    [JsonPropertyName("gpLastName")] public string GpLastName { get; set; } = "";
    [JsonPropertyName("gpEmail")] public string GpEmail { get; set; } = "";
    [JsonPropertyName("gpPhone")] public string GpPhone { get; set; } = "";
    [JsonPropertyName("providerNumber")] public string ProviderNumber { get; set; } = "";
    [JsonPropertyName("practiceName")] public string PracticeName { get; set; } = "";
    [JsonPropertyName("practiceAddress")] public string PracticeAddress { get; set; } = "";
    [JsonPropertyName("city")] public string City { get; set; } = "";
    [JsonPropertyName("state")] public string State { get; set; } = "";
    [JsonPropertyName("postcode")] public string Postcode { get; set; } = "";
    [JsonPropertyName("patientName")] public string PatientName { get; set; } = "";
    [JsonPropertyName("patientPhone")] public string PatientPhone { get; set; } = "";
    [JsonPropertyName("clinicalCondition")] public string ClinicalCondition { get; set; } = "";
    [JsonPropertyName("recaptchaToken")] public string? RecaptchaToken { get; set; }
    [JsonPropertyName("website")] public string? Website { get; set; } // honeypot — must stay empty
    [JsonPropertyName("formLoadedAt")] public long FormLoadedAt { get; set; } // client Date.now() at page load
}
