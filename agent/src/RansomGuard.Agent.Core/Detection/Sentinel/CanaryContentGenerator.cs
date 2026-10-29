using System.Security.Cryptography;
using System.Text;

namespace RansomGuard.Agent.Core.Detection.Sentinel;

/// <summary>
/// Generates realistic hospital records to use as canary files.
///
/// <para>
/// All of it is invented. No real patient data is involved, which is what makes the decoys
/// lawful to plant (GDPR, Cameroon Law 2024/017). It has to read like a genuine record anyway:
/// ransomware that skips files which do not look like documents would skip a decoy that reads
/// like a placeholder.
/// </para>
///
/// <para>
/// The content is English (owner's decision, 2026-10-25). The hospital is in Cameroon, so the
/// names stay Cameroonian -- they are names, not a language -- and the medicine stays what a
/// hospital in Yaounde actually treats.
/// </para>
/// </summary>
public static class CanaryContentGenerator
{
    private static readonly string[] CameroonianLastNames =
    [
        "Mballa", "Ngoa", "Kamga", "Tchamba", "Bekolo", "Etoundi", "Mvondo",
        "Nkoulou", "Fotso", "Tagne", "Njoya", "Biya", "Messi", "Atangana",
        "Onana", "Ekambi", "Nganou", "Tchakounte", "Nkeng", "Simo"
    ];

    private static readonly string[] CameroonianFirstNames =
    [
        "Jean-Pierre", "Marie-Claire", "Paul", "Francine", "Emmanuel", "Bernadette",
        "Samuel", "Cecile", "Joseph", "Anne-Marie", "Michel", "Therese",
        "David", "Isabelle", "Pierre", "Claudine", "Andre", "Marguerite",
        "Alain", "Solange"
    ];

    private static readonly string[] DoctorNames =
    [
        "Dr. Nkeng Patrice", "Dr. Fotso Mireille", "Dr. Kamga Bernard",
        "Dr. Tchamba Sylvie", "Dr. Mvondo Jean", "Dr. Atangana Rose",
        "Dr. Simo Etienne", "Dr. Njoya Marianne"
    ];

    private static readonly string[] MedicalConditions =
    [
        "Severe malaria (P. falciparum)", "Stage 2 hypertension",
        "Type 2 diabetes, non insulin-dependent", "Recurrent urinary tract infection",
        "Moderate iron-deficiency anaemia", "Chronic H. pylori gastritis",
        "Pulmonary tuberculosis (under treatment)", "Sickle cell disease SS",
        "Stage 3 chronic kidney disease", "Persistent bronchial asthma"
    ];

    private static readonly string[] Treatments =
    [
        "Artemether-Lumefantrine 80/480 mg, twice daily for 3 days",
        "Amlodipine 10 mg, once daily in the morning",
        "Metformin 850 mg, twice daily with meals",
        "Ciprofloxacin 500 mg, twice daily for 7 days",
        "Ferrous sulphate + folic acid, 1 tablet daily for 3 months",
        "Omeprazole 20 mg, once daily before breakfast",
        "Rifampicin + Isoniazid + Pyrazinamide (RHZ), national protocol",
        "Folic acid 5 mg/day + Hydroxycarbamide 15 mg/kg/day"
    ];

    /// <summary>
    /// The templates a canary can be generated from, and the names they used to have.
    ///
    /// <para>
    /// The names were French. A canary already planted on a workstation records the name it was
    /// made with, and that name is read again every time the decoy is checked or repaired, so
    /// dropping the old names would orphan every canary already in the field: the agent would
    /// fall through to the default template and quietly repair a laboratory result as a patient
    /// record. The old names are kept here and map to the same content. They are not offered to
    /// anything new -- appsettings.json lists the English ones -- so the field migrates as
    /// workstations are reinstalled, without a canary going unwatched in the meantime.
    /// </para>
    /// </summary>
    private static readonly Dictionary<string, Func<string>> Templates = new(StringComparer.OrdinalIgnoreCase)
    {
        ["patient_record"] = GeneratePatientRecord,
        ["lab_results"] = GenerateLabResults,
        ["medical_imaging"] = GenerateImagingReport,
        ["pharmacy_prescription"] = GeneratePrescription,
        ["consultation_report"] = GenerateConsultationReport,

        // The names used before 2026-10-25, kept so canaries already in the field stay watched.
        ["dossier_patient"] = GeneratePatientRecord,
        ["analyses_laboratoire"] = GenerateLabResults,
        ["imagerie_medicale"] = GenerateImagingReport,
        ["prescription_pharmacie"] = GeneratePrescription,
        ["rapport_consultation"] = GenerateConsultationReport,
    };

    /// <summary>
    /// Generates canary content for a template. Each call produces different content.
    /// </summary>
    /// <param name="template">Template name, for example "patient_record".</param>
    /// <returns>The content bytes and the file extension.</returns>
    public static (byte[] Content, string Extension) Generate(string template)
    {
        Func<string> generator = Templates.TryGetValue(template, out Func<string>? found)
            ? found
            : GeneratePatientRecord;

        return (Encoding.UTF8.GetBytes(generator()), ".txt");
    }

    /// <summary>The template names offered to anything new. The old names still work.</summary>
    public static IReadOnlyCollection<string> KnownTemplates =>
    [
        "patient_record", "lab_results", "medical_imaging",
        "pharmacy_prescription", "consultation_report"
    ];

    /// <summary>
    /// Computes SHA-256 hash of the given content bytes.
    /// </summary>
    public static string ComputeHash(byte[] content)
    {
        byte[] hash = SHA256.HashData(content);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string GeneratePatientRecord()
    {
        string firstName = Pick(CameroonianFirstNames);
        string lastName = Pick(CameroonianLastNames);
        string dob = RandomBirthDate();
        string fileNumber = $"HCY-{RandomInt(2020, 2026)}-{RandomInt(10000, 99999)}";

        return $"""
            ================================================================
                      CONFIDENTIAL MEDICAL RECORD
                      YAOUNDE CENTRAL HOSPITAL
            ================================================================

            Patient          : {lastName} {firstName}
            Date of birth    : {dob}
            Record number    : {fileNumber}
            Attending doctor : {Pick(DoctorNames)}
            Ward             : Internal Medicine

            ----------------------------------------------------------------
            MEDICAL HISTORY
            ----------------------------------------------------------------
              - {Pick(MedicalConditions)}
              - {Pick(MedicalConditions)}

            ----------------------------------------------------------------
            CURRENT TREATMENT
            ----------------------------------------------------------------
              - {Pick(Treatments)}
              - {Pick(Treatments)}

            ----------------------------------------------------------------
            FOLLOW-UP NOTES
            ----------------------------------------------------------------
            Consultation of {RandomRecentDate()}:
            The patient attends for a routine review. Clinical examination
            unremarkable. Blood pressure {RandomInt(110, 160)}/{RandomInt(60, 100)} mmHg.
            Temperature {RandomDecimal(36.0, 38.5)} C. Weight {RandomInt(50, 95)} kg.

            Next appointment in 4 weeks.

            Signed : {Pick(DoctorNames)}
            Date   : {RandomRecentDate()}

            -- This document is confidential. Unauthorised disclosure is an
               offence under Law No. 2024/017. --
            """;
    }

    private static string GenerateLabResults()
    {
        string firstName = Pick(CameroonianFirstNames);
        string lastName = Pick(CameroonianLastNames);

        return $"""
            ================================================================
                      LABORATORY RESULTS
                      CENTRAL LABORATORY - YAOUNDE HOSPITAL
            ================================================================

            Patient        : {lastName} {firstName}
            Sample number  : LAB-{RandomInt(2025, 2026)}-{RandomInt(100000, 999999)}
            Collected on   : {RandomRecentDate()}
            Requested by   : {Pick(DoctorNames)}

            ----------------------------------------------------------------
            FULL BLOOD COUNT
            ----------------------------------------------------------------
            Haemoglobin        : {RandomDecimal(7.0, 16.0)} g/dL      (ref 12.0 - 16.0)
            White cells        : {RandomDecimal(3.0, 15.0)} x10^9/L   (ref 4.0 - 10.0)
            Platelets          : {RandomInt(80, 450)} x10^9/L         (ref 150 - 400)
            Haematocrit        : {RandomDecimal(25.0, 48.0)} %        (ref 36 - 46)

            ----------------------------------------------------------------
            BIOCHEMISTRY
            ----------------------------------------------------------------
            Fasting glucose    : {RandomDecimal(0.7, 2.5)} g/L        (ref 0.70 - 1.10)
            Creatinine         : {RandomInt(6, 25)} mg/L              (ref 7 - 13)
            Urea               : {RandomDecimal(0.15, 0.80)} g/L      (ref 0.15 - 0.45)
            ALT                : {RandomInt(10, 90)} IU/L             (ref 10 - 40)

            ----------------------------------------------------------------
            PARASITOLOGY
            ----------------------------------------------------------------
            Thick blood film   : {Pick(["Negative", "Positive, P. falciparum", "Positive, low parasitaemia"])}
            Rapid malaria test : {Pick(["Negative", "Positive"])}

            Validated by : {Pick(DoctorNames)}
            Issued on    : {RandomRecentDate()}

            -- Results to be interpreted by the requesting clinician. --
            """;
    }

    private static string GenerateImagingReport()
    {
        string firstName = Pick(CameroonianFirstNames);
        string lastName = Pick(CameroonianLastNames);

        return $"""
            ================================================================
                      MEDICAL IMAGING REPORT
                      RADIOLOGY DEPARTMENT - YAOUNDE CENTRAL HOSPITAL
            ================================================================

            Patient       : {lastName} {firstName}
            Examination   : {Pick(["Chest X-ray (PA)", "Abdominal ultrasound", "Obstetric ultrasound", "Knee X-ray", "Head CT scan"])}
            Performed on  : {RandomRecentDate()}
            Radiologist   : {Pick(DoctorNames)}
            Reference     : IMG-{RandomInt(2025, 2026)}-{RandomInt(10000, 99999)}

            ----------------------------------------------------------------
            FINDINGS
            ----------------------------------------------------------------
            - Clear lung fields, no focal consolidation
            - No mediastinal widening
            - Cardiac silhouette within normal limits
            - Costophrenic angles clear
            - No visible bone lesion
            - Rib arches intact

            ----------------------------------------------------------------
            CONCLUSION
            ----------------------------------------------------------------
            {Pick([
                "Unremarkable examination.",
                "No abnormality of clinical significance.",
                "Appearances stable compared with the previous study.",
                "Clinical correlation recommended."
            ])}

            Reported by : {Pick(DoctorNames)}
            Date        : {RandomRecentDate()}

            -- Confidential. Part of the patient's medical record. --
            """;
    }

    private static string GeneratePrescription()
    {
        string firstName = Pick(CameroonianFirstNames);
        string lastName = Pick(CameroonianLastNames);

        return $"""
            ================================================================
                      MEDICAL PRESCRIPTION
                      YAOUNDE CENTRAL HOSPITAL
            ================================================================

            Patient : {lastName} {firstName}
            Age     : {RandomInt(18, 85)} years
            Date    : {RandomRecentDate()}

            ----------------------------------------------------------------
            PRESCRIBED
            ----------------------------------------------------------------
            1. {Pick(Treatments)}
            2. {Pick(Treatments)}
            3. Paracetamol 1000 mg - 1 tablet every 6 hours
               if pain or fever above 38.5 C (maximum 4 g per day)

            ----------------------------------------------------------------
            ADVICE
            ----------------------------------------------------------------
            - Take the full course even if symptoms improve
            - Return to clinic if symptoms worsen
            - Repeat blood tests in 2 weeks

            Prescriber     : {Pick(DoctorNames)}
            Medical council: CNOM/{RandomInt(1000, 9999)}

            -- Not to be repeated without a medical consultation. --
            """;
    }

    private static string GenerateConsultationReport()
    {
        string firstName = Pick(CameroonianFirstNames);
        string lastName = Pick(CameroonianLastNames);

        return $"""
            ================================================================
                      CONSULTATION REPORT
                      YAOUNDE CENTRAL HOSPITAL
            ================================================================

            Patient      : {lastName} {firstName}
            Date         : {RandomRecentDate()}
            Seen by      : {Pick(DoctorNames)}
            Presenting   : {Pick([
                "Fever for 5 days",
                "Acute abdominal pain",
                "Routine review after treatment",
                "Recurrent headache and dizziness"
            ])}

            ----------------------------------------------------------------
            EXAMINATION
            ----------------------------------------------------------------
            General condition : {Pick(["Good", "Reduced", "Preserved", "Slightly reduced"])}
            Temperature       : {RandomDecimal(36.0, 39.5)} C
            Blood pressure    : {RandomInt(100, 170)}/{RandomInt(55, 105)} mmHg
            Pulse             : {RandomInt(55, 115)} bpm
            Weight            : {RandomInt(45, 100)} kg

            ----------------------------------------------------------------
            WORKING DIAGNOSIS
            ----------------------------------------------------------------
            {Pick(MedicalConditions)}

            ----------------------------------------------------------------
            PLAN
            ----------------------------------------------------------------
            - Blood tests requested (FBC, CRP, thick film)
            - Symptomatic treatment started
            - Review in 1 week

            Doctor : {Pick(DoctorNames)}

            -- Confidential. Part of the patient's medical record. --
            """;
    }

    private static string Pick(string[] options) =>
        options[RandomNumberGenerator.GetInt32(options.Length)];

    private static int RandomInt(int minInclusive, int maxInclusive) =>
        RandomNumberGenerator.GetInt32(minInclusive, maxInclusive + 1);

    private static string RandomDecimal(double min, double max)
    {
        int scaled = RandomNumberGenerator.GetInt32((int)(min * 10), (int)(max * 10) + 1);
        return (scaled / 10.0).ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string RandomRecentDate() =>
        DateTime.UtcNow.AddDays(-RandomNumberGenerator.GetInt32(1, 400)).ToString("yyyy-MM-dd");

    private static string RandomBirthDate() =>
        DateTime.UtcNow.AddYears(-RandomNumberGenerator.GetInt32(18, 86))
            .AddDays(-RandomNumberGenerator.GetInt32(0, 365)).ToString("yyyy-MM-dd");
}
