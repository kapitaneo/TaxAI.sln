namespace ExtractionWorkflow.Models;

/// <summary>
/// Every message this module can return, as a fixed constant.
/// <para>
/// None is interpolated and none has a formatting hole, so no message can ever
/// carry a caller value, a document value, a file name, a content key, a
/// fingerprint, a provider payload, or a prompt. A message says which part of
/// the structure was wrong; a reader learns nothing about the taxpayer from it.
/// </para>
/// <para>
/// The engine's own messages are not copied here. When the existing parser,
/// mapper, or processor refuses something, its <c>ValidationError</c>s are
/// surfaced verbatim and in order — re-wording an engine error is how two
/// descriptions of the same tax rule start to disagree.
/// </para>
/// </summary>
public static class ExtractionWorkflowErrors
{
    public const string ProviderResultAbsent =
        "The document intelligence provider produced no result for this document.";

    public const string PayloadBoxesAbsent =
        "The provider result declared no extracted box collection.";

    public const string PayloadBoxAbsent =
        "The provider result declared an absent entry in its extracted box collection.";

    public const string PayloadBoxCodeAbsent =
        "An extracted box carried no box code.";
}
