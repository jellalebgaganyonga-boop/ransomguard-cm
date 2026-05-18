using System.Security.Cryptography;
using System.Text;

namespace RansomGuard.Agent.Core.Detection.Sentinel;

/// <summary>
/// Generates realistic semantic medical canary file content in French and English.
/// All content is synthetic — zero real patient data (GDPR/Law 2024/017 compliant).
/// Content is designed to look like genuine hospital records to defeat heuristic filters.
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
        "Jean-Pierre", "Marie-Claire", "Paul", "Françoise", "Emmanuel", "Bernadette",
        "Samuel", "Cécile", "Joseph", "Anne-Marie", "Michel", "Thérèse",
        "David", "Isabelle", "Pierre", "Claudine", "André", "Marguerite",
        "Alain", "Solange"
    ];

    private static readonly string[] DoctorNames =
    [
        "Dr. Nkeng Patrice", "Dr. Fotso Mireille", "Dr. Kamga Bernard",
        "Dr. Tchamba Sylvie", "Dr. Mvondo Jean", "Dr. Atangana Rose",
        "Dr. Simo Étienne", "Dr. Njoya Marianne"
    ];

    private static readonly string[] MedicalConditions =
    [
        "Paludisme sévère (P. falciparum)", "Hypertension artérielle stade 2",
        "Diabète de type 2 non insulinodépendant", "Infection urinaire récurrente",
        "Anémie ferriprive modérée", "Gastrite chronique à H. pylori",
        "Tuberculose pulmonaire (en traitement)", "Drépanocytose SS",
        "Insuffisance rénale chronique stade 3", "Asthme bronchique persistant"
    ];

    private static readonly string[] Treatments =
    [
        "Artéméther-Luméfantrine 80/480mg, 2x/jour pendant 3 jours",
        "Amlodipine 10mg, 1x/jour le matin",
        "Metformine 850mg, 2x/jour aux repas",
        "Ciprofloxacine 500mg, 2x/jour pendant 7 jours",
        "Fer Foldine 1cp/jour pendant 3 mois",
        "Oméprazole 20mg, 1x/jour avant le petit-déjeuner",
        "Rifampicine + Isoniazide + Pyrazinamide (RHZ), protocole national",
        "Acide folique 5mg/jour + Hydroxycarbamide 15mg/kg/jour"
    ];

    /// <summary>
    /// Generates canary content for the specified template.
    /// Each call produces unique content using randomized data.
    /// </summary>
    /// <param name="template">Template name (e.g., "dossier_patient").</param>
    /// <returns>Tuple of (content bytes, file extension).</returns>
    public static (byte[] Content, string Extension) Generate(string template)
    {
        string content = template switch
        {
            "dossier_patient" => GeneratePatientRecord(),
            "analyses_laboratoire" => GenerateLabAnalysis(),
            "imagerie_medicale" => GenerateImagingReport(),
            "prescription_pharmacie" => GeneratePrescription(),
            "rapport_consultation" => GenerateConsultationReport(),
            _ => GeneratePatientRecord()
        };

        return (Encoding.UTF8.GetBytes(content), ".txt");
    }

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
        string dob = RandomDate(1950, 2005);
        string fileNumber = $"DM-{RandomInt(10000, 99999)}-{RandomInt(100, 999)}";

        return $"""
            ╔══════════════════════════════════════════════════════════════╗
            ║          DOSSIER MÉDICAL CONFIDENTIEL                        ║
            ║          HÔPITAL CENTRAL DE YAOUNDÉ                          ║
            ╚══════════════════════════════════════════════════════════════╝

            Patient         : {lastName} {firstName}
            Date de naissance : {dob}
            Numéro de dossier : {fileNumber}
            Médecin traitant  : {Pick(DoctorNames)}
            Service           : Médecine Interne

            ─────────────────────────────────────────────────────────────
            ANTÉCÉDENTS MÉDICAUX
            ─────────────────────────────────────────────────────────────
              - {Pick(MedicalConditions)}
              - {Pick(MedicalConditions)}

            ─────────────────────────────────────────────────────────────
            TRAITEMENT EN COURS
            ─────────────────────────────────────────────────────────────
              - {Pick(Treatments)}
              - {Pick(Treatments)}

            ─────────────────────────────────────────────────────────────
            NOTES DE SUIVI
            ─────────────────────────────────────────────────────────────
            Consultation du {RandomRecentDate()} :
            Le patient se présente pour un contrôle de routine. Examen clinique
            sans particularité. Tension artérielle {RandomInt(110, 160)}/{RandomInt(60, 100)} mmHg.
            Température {RandomDecimal(36.0, 38.5)}°C. Poids {RandomInt(50, 95)} kg.

            Prochaine consultation prévue dans 4 semaines.

            Signature : {Pick(DoctorNames)}
            Date : {RandomRecentDate()}

            ── Ce document est confidentiel. Toute divulgation non autorisée
               est sanctionnée par la Loi N°2024/017. ──
            """;
    }

    private static string GenerateLabAnalysis()
    {
        string firstName = Pick(CameroonianFirstNames);
        string lastName = Pick(CameroonianLastNames);

        return $"""
            ╔══════════════════════════════════════════════════════════════╗
            ║          RÉSULTATS D'ANALYSES DE LABORATOIRE                ║
            ║          LABORATOIRE CENTRAL — HÔPITAL DE YAOUNDÉ           ║
            ╚══════════════════════════════════════════════════════════════╝

            Patient     : {lastName} {firstName}
            N° Analyse  : LAB-{RandomInt(100000, 999999)}
            Date prélèv.: {RandomRecentDate()}
            Prescripteur: {Pick(DoctorNames)}

            ─────────────────────────────────────────────────────────────
            HÉMATOLOGIE COMPLÈTE
            ─────────────────────────────────────────────────────────────
            Hémoglobine      : {RandomDecimal(8.0, 16.0)} g/dL    (N: 12-16)
            Hématocrite      : {RandomDecimal(30.0, 50.0)} %       (N: 36-46)
            Globules blancs  : {RandomDecimal(3.5, 12.0)} x10³/µL  (N: 4.0-10.0)
            Plaquettes       : {RandomInt(100, 400)} x10³/µL       (N: 150-400)
            VGM              : {RandomDecimal(75.0, 100.0)} fL     (N: 80-100)

            ─────────────────────────────────────────────────────────────
            BIOCHIMIE
            ─────────────────────────────────────────────────────────────
            Glycémie à jeun  : {RandomDecimal(0.7, 2.5)} g/L       (N: 0.7-1.1)
            Créatinine       : {RandomDecimal(6.0, 25.0)} mg/L     (N: 6-13)
            Urée             : {RandomDecimal(0.15, 0.60)} g/L     (N: 0.15-0.45)
            ASAT             : {RandomInt(10, 80)} UI/L             (N: 10-40)
            ALAT             : {RandomInt(10, 70)} UI/L             (N: 10-40)

            ─────────────────────────────────────────────────────────────
            PARASITOLOGIE
            ─────────────────────────────────────────────────────────────
            Goutte épaisse   : {(RandomInt(0, 1) == 1 ? "Positive — P. falciparum, densité parasitaire 2500/µL" : "Négative")}
            TDR Paludisme    : {(RandomInt(0, 1) == 1 ? "Positif (Pf)" : "Négatif")}

            Biologiste responsable : Dr. {Pick(CameroonianLastNames)} {Pick(CameroonianFirstNames)[..1]}.
            Date validation : {RandomRecentDate()}
            """;
    }

    private static string GenerateImagingReport()
    {
        string firstName = Pick(CameroonianFirstNames);
        string lastName = Pick(CameroonianLastNames);

        return $"""
            ╔══════════════════════════════════════════════════════════════╗
            ║          COMPTE-RENDU D'IMAGERIE MÉDICALE                   ║
            ║          SERVICE DE RADIOLOGIE — HÔPITAL DE YAOUNDÉ         ║
            ╚══════════════════════════════════════════════════════════════╝

            Patient      : {lastName} {firstName}
            N° Examen    : RAD-{RandomInt(10000, 99999)}
            Date examen  : {RandomRecentDate()}
            Prescripteur : {Pick(DoctorNames)}
            Type examen  : Radiographie thoracique (Face + Profil)

            ─────────────────────────────────────────────────────────────
            RÉSULTATS
            ─────────────────────────────────────────────────────────────
            Parenchyme pulmonaire :
              - Transparence pulmonaire conservée bilatéralement
              - Pas d'opacité alvéolaire ni interstitielle
              - Culs-de-sac costo-diaphragmatiques libres

            Médiastin :
              - Silhouette cardiaque de taille normale (ICT = 0.{RandomInt(42, 55)})
              - Pas d'élargissement médiastinal

            Structures osseuses :
              - Pas de lésion osseuse visible
              - Intégrité des arcs costaux

            CONCLUSION : Radiographie thoracique sans anomalie significative.

            Radiologue : {Pick(DoctorNames)}
            Date compte-rendu : {RandomRecentDate()}
            """;
    }

    private static string GeneratePrescription()
    {
        string firstName = Pick(CameroonianFirstNames);
        string lastName = Pick(CameroonianLastNames);

        return $"""
            ╔══════════════════════════════════════════════════════════════╗
            ║          ORDONNANCE MÉDICALE                                ║
            ║          HÔPITAL CENTRAL DE YAOUNDÉ                          ║
            ╚══════════════════════════════════════════════════════════════╝

            Patient      : {lastName} {firstName}
            Âge          : {RandomInt(18, 85)} ans
            Poids        : {RandomInt(45, 100)} kg
            Date         : {RandomRecentDate()}

            ─────────────────────────────────────────────────────────────
            PRESCRIPTION
            ─────────────────────────────────────────────────────────────

            1. {Pick(Treatments)}

            2. {Pick(Treatments)}

            3. Paracétamol 1000mg — 1 comprimé toutes les 6 heures
               si douleur ou fièvre > 38.5°C (max 4g/jour)

            ─────────────────────────────────────────────────────────────
            RECOMMANDATIONS
            ─────────────────────────────────────────────────────────────
            - Respecter les horaires de prise
            - Revenir en consultation si aggravation des symptômes
            - Contrôle biologique dans 2 semaines

            Prescripteur : {Pick(DoctorNames)}
            N° Ordre médecins : CNOM/{RandomInt(1000, 9999)}

            ── Ne pas renouveler sans consultation médicale ──
            """;
    }

    private static string GenerateConsultationReport()
    {
        string firstName = Pick(CameroonianFirstNames);
        string lastName = Pick(CameroonianLastNames);

        return $"""
            ╔══════════════════════════════════════════════════════════════╗
            ║          RAPPORT DE CONSULTATION                            ║
            ║          HÔPITAL CENTRAL DE YAOUNDÉ                          ║
            ╚══════════════════════════════════════════════════════════════╝

            Patient      : {lastName} {firstName}
            N° Dossier   : DM-{RandomInt(10000, 99999)}-{RandomInt(100, 999)}
            Date consult.: {RandomRecentDate()}
            Motif        : {Pick(["Fièvre persistante depuis 5 jours", "Douleur abdominale aiguë", "Contrôle de routine post-traitement", "Céphalées et vertiges récurrents", "Toux productive depuis 3 semaines"])}

            ─────────────────────────────────────────────────────────────
            EXAMEN CLINIQUE
            ─────────────────────────────────────────────────────────────
            État général : {Pick(["Bon", "Altéré", "Conservé", "Légèrement altéré"])}
            Température  : {RandomDecimal(36.0, 39.5)}°C
            TA           : {RandomInt(100, 170)}/{RandomInt(60, 100)} mmHg
            FC           : {RandomInt(55, 110)} bpm
            FR           : {RandomInt(14, 28)} cycles/min
            SpO2         : {RandomInt(90, 99)} %

            ─────────────────────────────────────────────────────────────
            HYPOTHÈSE DIAGNOSTIQUE
            ─────────────────────────────────────────────────────────────
              - {Pick(MedicalConditions)}

            ─────────────────────────────────────────────────────────────
            CONDUITE À TENIR
            ─────────────────────────────────────────────────────────────
              - Bilan biologique demandé (NFS, CRP, goutte épaisse)
              - Traitement symptomatique instauré
              - Rendez-vous de contrôle dans 1 semaine

            Médecin : {Pick(DoctorNames)}
            Signature : ____________________
            """;
    }

    private static string Pick(string[] array)
    {
        return array[RandomNumberGenerator.GetInt32(array.Length)];
    }

    private static int RandomInt(int min, int max)
    {
        return RandomNumberGenerator.GetInt32(min, max + 1);
    }

    private static string RandomDecimal(double min, double max)
    {
        double value = min + (max - min) * RandomNumberGenerator.GetInt32(0, 1000) / 1000.0;
        return value.ToString("F1");
    }

    private static string RandomDate(int minYear, int maxYear)
    {
        int year = RandomInt(minYear, maxYear);
        int month = RandomInt(1, 12);
        int day = RandomInt(1, 28);
        return $"{day:D2}/{month:D2}/{year}";
    }

    private static string RandomRecentDate()
    {
        DateTime recent = DateTime.Now.AddDays(-RandomInt(1, 90));
        return recent.ToString("dd/MM/yyyy");
    }
}
