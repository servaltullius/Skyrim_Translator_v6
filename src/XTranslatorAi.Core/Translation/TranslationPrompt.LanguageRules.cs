using System.Text;

namespace XTranslatorAi.Core.Translation;

public static partial class TranslationPrompt
{
    private static void AppendMeaningAndTerminologyRules(StringBuilder sb)
    {
        sb.AppendLine("- Preserve numbers, units, signs, percentages, negation, conditions and who acts on whom. A 100% chance is not a 100% increase; a per-unit value is not a total.");
        sb.AppendLine("- A stated chance of an event is its probability, including when the value is a placeholder. Express an increase only when the source explicitly says increase, bonus or additional; do not invent an increase from the verb gain.");
        sb.AppendLine("- Apply glossary entries only to the matching meaning and grammatical use. A shared substring is not a match: a noun entry for Trigger must not turn the verb triggers into an unnatural noun-based phrase.");
    }

    private static void AppendKoreanGrammarRules(StringBuilder sb)
    {
        sb.AppendLine("- Korean grammar: Choose particles by Korean pronunciation, including retained acronyms (e.g. NPC는, NPC를). Keep identifiers and key labels unchanged when appropriate; do not choose particles by the final Latin letter alone.");
        sb.AppendLine("- When avoiding particles on numeric tokens, restructure the sentence around a noun; do not drop required particles or connecting words. For example, prefer 치명타 확률이 __XT_PH_NUM_0000__ 증가합니다 over an unfinished 보너스 __XT_PH_NUM_0000__ 제공 phrase.");
        sb.AppendLine("- Do NOT output ambiguous particle markers like \"을(를)\", \"(을)를\", \"은(는)\", \"(은)는\", \"이(가)\", \"(이)가\", \"와(과)\", \"(와)과\", or \"(으)로\". Choose one correct form.");
    }

    private static void AppendKoreanProbabilityExamples(StringBuilder sb)
    {
        sb.AppendLine("Probability examples (reference only; do not copy their tokens into other items):");
        sb.AppendLine("- Gain a __XT_PH_NUM_0000__ chance of a critical hit. => __XT_PH_NUM_0000__ 확률로 치명타가 발생합니다.");
        sb.AppendLine("- Increase critical hit chance by __XT_PH_NUM_0000__. => 치명타 확률이 __XT_PH_NUM_0000__ 증가합니다.");
        sb.AppendLine("- Gain a bonus __XT_PH_NUM_0000__ critical hit chance. => 치명타 확률이 __XT_PH_NUM_0000__ 추가로 증가합니다.");
    }
}
