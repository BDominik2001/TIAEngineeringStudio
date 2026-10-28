namespace TiaStudio.Contracts
{
    /// <summary>
    /// Stabil hibakódok; a contracts/error-codes.json tükre (egy unit teszt ellenőrzi az egyezést).
    /// </summary>
    public static class ErrorCodes
    {
        public const string InvalidJson = "INVALID_JSON";
        public const string InputTooLarge = "INPUT_TOO_LARGE";
        public const string UnsupportedSchemaVersion = "UNSUPPORTED_SCHEMA_VERSION";
        public const string MissingField = "MISSING_FIELD";
        public const string NullValue = "NULL_VALUE";
        public const string UnknownField = "UNKNOWN_FIELD";
        public const string InvalidType = "INVALID_TYPE";
        public const string InvalidValue = "INVALID_VALUE";
        public const string NonAscii = "NON_ASCII";
        public const string InvalidIdentifier = "INVALID_IDENTIFIER";
        public const string UnsupportedTiaVersion = "UNSUPPORTED_TIA_VERSION";
        public const string UnsupportedCpuFamily = "UNSUPPORTED_CPU_FAMILY";
        public const string NoDevices = "NO_DEVICES";
        public const string TooManyDevices = "TOO_MANY_DEVICES";
        public const string UnsupportedDeviceKind = "UNSUPPORTED_DEVICE_KIND";
        public const string DuplicateDeviceId = "DUPLICATE_DEVICE_ID";
        public const string InvalidIoAddress = "INVALID_IO_ADDRESS";
        public const string IoAddressOutOfRange = "IO_ADDRESS_OUT_OF_RANGE";
        public const string DuplicateIoAddress = "DUPLICATE_IO_ADDRESS";
        public const string GeneratedNameTooLong = "GENERATED_NAME_TOO_LONG";
        public const string GeneratedNameCollision = "GENERATED_NAME_COLLISION";
        public const string IoHwNotVerified = "IO_HW_NOT_VERIFIED";

        public const string OutputExists = "OUTPUT_EXISTS";
        public const string TemplateIntegrity = "TEMPLATE_INTEGRITY";
        public const string NonAsciiSource = "NON_ASCII_SOURCE";
        public const string GeneratorFailed = "GENERATOR_FAILED";
        public const string ValidatorUnavailable = "VALIDATOR_UNAVAILABLE";

        public const string ManifestInvalid = "MANIFEST_INVALID";
        public const string PathEscape = "PATH_ESCAPE";
        public const string HashMismatch = "HASH_MISMATCH";
        public const string ApprovalInvalid = "APPROVAL_INVALID";

        public const string CommandInvalid = "COMMAND_INVALID";
        public const string ProtocolVersionUnsupported = "PROTOCOL_VERSION_UNSUPPORTED";
        public const string OperationNotSupported = "OPERATION_NOT_SUPPORTED";
        public const string WorkerVersionMismatch = "WORKER_VERSION_MISMATCH";

        public const string TiaNotInstalled = "TIA_NOT_INSTALLED";
        public const string ApiDllMismatch = "API_DLL_MISMATCH";
        public const string WorkerBuiltWithoutOpenness = "WORKER_BUILT_WITHOUT_OPENNESS";
        public const string OpennessAccessDenied = "OPENNESS_ACCESS_DENIED";
        public const string LicenseMissing = "LICENSE_MISSING";
        public const string UmacPermissionDenied = "UMAC_PERMISSION_DENIED";

        public const string ProjectNotFound = "PROJECT_NOT_FOUND";
        public const string ProjectVersionMismatch = "PROJECT_VERSION_MISMATCH";
        public const string ProjectNotSandbox = "PROJECT_NOT_SANDBOX";
        public const string ProjectUpgradeRequired = "PROJECT_UPGRADE_REQUIRED";
        public const string PlcNotFound = "PLC_NOT_FOUND";
        public const string PlcOnline = "PLC_ONLINE";
        public const string BlockCollision = "BLOCK_COLLISION";
        public const string ImportFailed = "IMPORT_FAILED";
        public const string CompileError = "COMPILE_ERROR";

        public const string Timeout = "TIMEOUT";
        public const string WorkerCrashed = "WORKER_CRASHED";
        public const string Cancelled = "CANCELLED";
        public const string WorkflowStateInvalid = "WORKFLOW_STATE_INVALID";
        public const string InternalError = "INTERNAL_ERROR";
    }
}
