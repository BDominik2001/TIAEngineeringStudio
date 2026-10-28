using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using TiaStudio.Application.Ports;
using TiaStudio.Contracts;

namespace TiaStudio.Infrastructure.Native
{
    /// <summary>
    /// A Rust tes_validator natív DLL P/Invoke-burkolója (src/rust/validator/include/tes_validator.h).
    /// A DLL csak validációs bemenetet kap; fájlrendszer- vagy TIA-jogosultsága nincs.
    /// Windows alatt tes_validator.dll, Linuxon (CI) libtes_validator.so; x64 kötelező.
    /// </summary>
    public sealed class RustStaticValidator : IStaticValidator
    {
        public const uint ExpectedAbiVersion = 1;
        private const string Library = "tes_validator";

        public ValidationReport Validate(byte[] json)
        {
            uint abi;
            try
            {
                abi = NativeMethods.tes_abi_version();
            }
            catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException || ex is BadImageFormatException)
            {
                return Unavailable("Rust validator library cannot be loaded: " + ex.Message);
            }
            if (abi != ExpectedAbiVersion)
            {
                return Unavailable("Rust validator ABI version " + abi + " != expected " + ExpectedAbiVersion);
            }

            IntPtr output = IntPtr.Zero;
            UIntPtr outputLength = UIntPtr.Zero;
            GCHandle pinned = GCHandle.Alloc(json, GCHandleType.Pinned);
            try
            {
                int status = NativeMethods.tes_validate(pinned.AddrOfPinnedObject(), new UIntPtr((uint)json.Length), out output, out outputLength);
                if (status != 0 || output == IntPtr.Zero)
                {
                    return Unavailable("tes_validate returned status " + status);
                }
                int length = checked((int)outputLength.ToUInt64());
                byte[] buffer = new byte[length];
                Marshal.Copy(output, buffer, 0, length);
                return ProtocolJson.DeserializeLenient<ValidationReport>(Encoding.UTF8.GetString(buffer));
            }
            finally
            {
                pinned.Free();
                if (output != IntPtr.Zero)
                {
                    NativeMethods.tes_free(output, outputLength);
                }
            }
        }

        private static ValidationReport Unavailable(string message)
        {
            return new ValidationReport
            {
                Validator = "tes_validator",
                Ok = false,
                ErrorCount = 1,
                Issues = new List<ValidationIssue> { new ValidationIssue(ValidationIssue.SeverityError, ErrorCodes.ValidatorUnavailable, "$", message) },
            };
        }

        private static class NativeMethods
        {
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
            public static extern uint tes_abi_version();

            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
            public static extern int tes_validate(IntPtr input, UIntPtr inputLength, out IntPtr outJson, out UIntPtr outLength);

            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
            public static extern void tes_free(IntPtr pointer, UIntPtr length);
        }
    }
}
