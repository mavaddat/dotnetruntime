// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace System.DirectoryServices.ActiveDirectory
{
    internal struct Component
    {
        public string? Name;
        public string? Value;
    }

    internal enum Capability : int
    {
        ActiveDirectory = 0,
        ActiveDirectoryApplicationMode = 1,
        ActiveDirectoryOrADAM = 2
    }

    internal enum SidType
    {
        RealObject = 0,        // Account SID (S-1-5-21-....)
        RealObjectFakeDomain = 1,        // BUILTIN SID (S-1-5-32-....)
        FakeObject = 2         // everything else: S-1-1-0 (\Everyone), S-1-2-0 (\LOCAL),
                               //   S-1-5-X for X != 21 and X != 32 (NT AUTHORITY), etc.
    }

    internal struct SupportedCapability
    {
        public const string ADOid = "1.2.840.113556.1.4.800";
        public const string ADAMOid = "1.2.840.113556.1.4.1851";
    }

    internal sealed class Utils
    {
        private const int LOGON32_LOGON_NEW_CREDENTIALS = 9;
        private const int LOGON32_PROVIDER_WINNT50 = 3;

        internal const AuthenticationTypes DefaultAuthType = AuthenticationTypes.Secure | AuthenticationTypes.Signing | AuthenticationTypes.Sealing;

        /*

        #define LANG_ENGLISH                     0x09
        #define SUBLANG_ENGLISH_US               0x01    // English (USA)
        #define SORT_DEFAULT                     0x0     // sorting default

        #define NORM_IGNORECASE           0x00000001  // ignore case
        #define NORM_IGNORENONSPACE       0x00000002  // ignore nonspacing chars
        #define NORM_IGNORESYMBOLS        0x00000004  // ignore symbols
        #define NORM_IGNOREKANATYPE       0x00010000  // ignore kanatype
        #define NORM_IGNOREWIDTH          0x00020000  // ignore width

        #define SORT_STRINGSORT           0x00001000  // use string sort method

        #define MAKELANGID(p, s)       ((((WORD  )(s)) << 10) | (WORD  )(p))

        #define MAKELCID(lgid, srtid)  ((DWORD)((((DWORD)((WORD  )(srtid))) << 16) |  \
                                             ((DWORD)((WORD  )(lgid)))))

        #define DS_DEFAULT_LOCALE                                           \
                            (MAKELCID(MAKELANGID(LANG_ENGLISH, SUBLANG_ENGLISH_US),  \
                            SORT_DEFAULT))

        #define DS_DEFAULT_LOCALE_COMPARE_FLAGS    (NORM_IGNORECASE     |   \
                                                    NORM_IGNOREKANATYPE |   \
                                                    NORM_IGNORENONSPACE |   \
                                                    NORM_IGNOREWIDTH    |   \
                                                    SORT_STRINGSORT )

        */
        private const uint LANG_ENGLISH = 0x09;
        private const uint SUBLANG_ENGLISH_US = 0x01;
        private const uint SORT_DEFAULT = 0x0;
        private const uint LANGID = ((uint)((((ushort)(SUBLANG_ENGLISH_US)) << 10) | (ushort)(LANG_ENGLISH)));
        private const uint LCID = ((uint)((((uint)((ushort)(SORT_DEFAULT))) << 16) | ((uint)((ushort)(LANGID)))));

        internal const uint NORM_IGNORECASE = 0x00000001;
        internal const uint NORM_IGNORENONSPACE = 0x00000002;
        internal const uint NORM_IGNOREKANATYPE = 0x00010000;
        internal const uint NORM_IGNOREWIDTH = 0x00020000;
        internal const uint SORT_STRINGSORT = 0x00001000;
        internal const uint DEFAULT_CMP_FLAGS = NORM_IGNORECASE |
                                                NORM_IGNOREKANATYPE |
                                                NORM_IGNORENONSPACE |
                                                NORM_IGNOREWIDTH |
                                                SORT_STRINGSORT;

        // To disable public/protected constructors for this class
        private Utils() { }

        internal static unsafe string GetDnsNameFromDN(string distinguishedName)
        {
            int result = 0;
            string? dnsName = null;
            IntPtr results = IntPtr.Zero;

            Debug.Assert(distinguishedName != null);

            // call DsCrackNamesW
            /*DWORD DsCrackNames(
                HANDLE hDS,
                DS_NAME_FLAGS flags,
                DS_NAME_FORMAT formatOffered,
                DS_NAME_FORMAT formatDesired,
                DWORD cNames,
                LPTSTR* rpNames,
                PDS_NAME_RESULT* ppResult
                );*/
            var dsCrackNames = (delegate* unmanaged<IntPtr, int, int, int, int, IntPtr, IntPtr*, int>)global::Interop.Kernel32.GetProcAddress(DirectoryContext.ADHandle, "DsCrackNamesW");
            if (dsCrackNames == null)
            {
                throw ExceptionHelper.GetExceptionFromErrorCode(Marshal.GetLastPInvokeError());
            }

            IntPtr name = Marshal.StringToHGlobalUni(distinguishedName);
            IntPtr ptr = Marshal.AllocHGlobal(IntPtr.Size);
            Marshal.WriteIntPtr(ptr, name);
            result = dsCrackNames(IntPtr.Zero, NativeMethods.DS_NAME_FLAG_SYNTACTICAL_ONLY,
                   NativeMethods.DS_FQDN_1779_NAME, NativeMethods.DS_CANONICAL_NAME, 1, ptr, &results);
            if (result == 0)
            {
                try
                {
                    DsNameResult dsNameResult = new DsNameResult();
                    Marshal.PtrToStructure(results, dsNameResult);
                    if ((dsNameResult.itemCount >= 1) && (dsNameResult.items != IntPtr.Zero))
                    {
                        DsNameResultItem dsNameResultItem = new DsNameResultItem();
                        Marshal.PtrToStructure(dsNameResult.items, dsNameResultItem);

                        if (dsNameResultItem.status == NativeMethods.DS_NAME_ERROR_NO_SYNTACTICAL_MAPPING ||
                            dsNameResultItem.name == null)
                        {
                            throw new ArgumentException(SR.InvalidDNFormat, nameof(distinguishedName));
                        }
                        else if (dsNameResultItem.status != 0)
                        {
                            // it is only syntatic mapping, we don't go on the wire
                            throw ExceptionHelper.GetExceptionFromErrorCode(result);
                        }

                        if ((dsNameResultItem.name.Length - 1) == dsNameResultItem.name.IndexOf('/'))
                        {
                            dnsName = dsNameResultItem.name.Substring(0, dsNameResultItem.name.Length - 1);
                        }
                        else
                        {
                            dnsName = dsNameResultItem.name;
                        }
                    }
                }
                finally
                {
                    if (ptr != (IntPtr)0)
                        Marshal.FreeHGlobal(ptr);

                    if (name != (IntPtr)0)
                        Marshal.FreeHGlobal(name);

                    // free the results
                    if (results != IntPtr.Zero)
                    {
                        // call DsFreeNameResultW
                        var dsFreeNameResultW = (delegate* unmanaged<IntPtr, void>)global::Interop.Kernel32.GetProcAddress(DirectoryContext.ADHandle, "DsFreeNameResultW");
                        if (dsFreeNameResultW == null)
                        {
                            throw ExceptionHelper.GetExceptionFromErrorCode(Marshal.GetLastPInvokeError());
                        }
                        dsFreeNameResultW(results);
                    }
                }
            }
            else if (result == NativeMethods.DS_NAME_ERROR_NO_SYNTACTICAL_MAPPING)
            {
                throw new ArgumentException(SR.InvalidDNFormat, nameof(distinguishedName));
            }
            else
            {
                // it is only syntatic mapping, we don't go on the wire
                throw ExceptionHelper.GetExceptionFromErrorCode(result);
            }

            return dnsName!;
        }

        internal static unsafe string GetDNFromDnsName(string dnsName)
        {
            int result = 0;
            string? dn = null;
            IntPtr results = IntPtr.Zero;

            Debug.Assert(dnsName != null);

            // call DsCrackNamesW
            /*DWORD DsCrackNames(
                HANDLE hDS,
                DS_NAME_FLAGS flags,
                DS_NAME_FORMAT formatOffered,
                DS_NAME_FORMAT formatDesired,
                DWORD cNames,
                LPTSTR* rpNames,
                PDS_NAME_RESULT* ppResult
                );*/
            var dsCrackNames = (delegate* unmanaged<IntPtr, int, int, int, int, IntPtr, IntPtr*, int>)global::Interop.Kernel32.GetProcAddress(DirectoryContext.ADHandle, "DsCrackNamesW");
            if (dsCrackNames == null)
            {
                throw ExceptionHelper.GetExceptionFromErrorCode(Marshal.GetLastPInvokeError());
            }
            IntPtr name = Marshal.StringToHGlobalUni(dnsName + "/");
            IntPtr ptr = Marshal.AllocHGlobal(IntPtr.Size);
            Marshal.WriteIntPtr(ptr, name);
            result = dsCrackNames(IntPtr.Zero, NativeMethods.DS_NAME_FLAG_SYNTACTICAL_ONLY,
                         NativeMethods.DS_CANONICAL_NAME, NativeMethods.DS_FQDN_1779_NAME, 1, ptr, &results);
            if (result == 0)
            {
                try
                {
                    DsNameResult dsNameResult = new DsNameResult();
                    Marshal.PtrToStructure(results, dsNameResult);

                    if ((dsNameResult.itemCount >= 1) && (dsNameResult.items != IntPtr.Zero))
                    {
                        DsNameResultItem dsNameResultItem = new DsNameResultItem();
                        Marshal.PtrToStructure(dsNameResult.items, dsNameResultItem);
                        dn = dsNameResultItem.name;
                    }
                }
                finally
                {
                    if (ptr != (IntPtr)0)
                        Marshal.FreeHGlobal(ptr);

                    if (name != (IntPtr)0)
                        Marshal.FreeHGlobal(name);
                    // free the results
                    if (results != IntPtr.Zero)
                    {
                        // call DsFreeNameResultW
                        var dsFreeNameResultW = (delegate* unmanaged<IntPtr, void>)global::Interop.Kernel32.GetProcAddress(DirectoryContext.ADHandle, "DsFreeNameResultW");
                        if (dsFreeNameResultW == null)
                        {
                            throw ExceptionHelper.GetExceptionFromErrorCode(Marshal.GetLastPInvokeError());
                        }
                        dsFreeNameResultW(results);
                    }
                }
            }
            else if (result == NativeMethods.DS_NAME_ERROR_NO_SYNTACTICAL_MAPPING)
            {
                throw new ArgumentException(SR.InvalidDNFormat);
            }
            else
            {
                // it is only syntatic mapping, we don't go on the wire
                throw ExceptionHelper.GetExceptionFromErrorCode(result);
            }

            return dn!;
        }

        //
        // DN is of the form cn=NTDS Settings, cn={dc name}, cn=Servers, cn={site name}, cn=Sites,
        //                                cn=Configuration, {defaultNamingContext}
        //    Bind to the NTDS-DSA (parent) object for and get the dnsHostName
        //    from there
        //
        internal static string GetDnsHostNameFromNTDSA(DirectoryContext context, string dn)
        {
            string? dcName = null;
            int index = dn.IndexOf(',');
            if (index == -1)
            {
                throw new ArgumentException(SR.InvalidDNFormat, nameof(dn));
            }

            // get parent name simply by removing the first component
            string bindingDN = dn.Substring(index + 1);
            DirectoryEntry de = DirectoryEntryManager.GetDirectoryEntry(context, bindingDN);
            try
            {
                // the "dnsHostName" attribute contains the dns name of the computer
                dcName = (string)PropertyManager.GetPropertyValue(context, de, PropertyManager.DnsHostName)!;
            }
            finally
            {
                de.Dispose();
            }
            return dcName;
        }

        internal static string GetAdamDnsHostNameFromNTDSA(DirectoryContext context, string dn)
        {
            string? dnsHostName = null;
            int ldapPort = -1;
            string ntdsaDn = dn;
            string serverDn = GetPartialDN(dn, 1);
            string serversDn = GetPartialDN(dn, 2);
            string ntdsdsa = "CN=NTDS-DSA";

            DirectoryEntry serversEntry = DirectoryEntryManager.GetDirectoryEntry(context, serversDn);

            string filter = "(|(&(" + PropertyManager.ObjectCategory + "=server)(" + PropertyManager.DistinguishedName + "=" + GetEscapedFilterValue(serverDn) + "))" +
                            "(&(" + PropertyManager.ObjectCategory + "=nTDSDSA)(" + PropertyManager.DistinguishedName + "=" + GetEscapedFilterValue(ntdsaDn) + ")))";
            string[] propertiesToLoad = new string[3];
            propertiesToLoad[0] = PropertyManager.DnsHostName;
            propertiesToLoad[1] = PropertyManager.MsDSPortLDAP;
            propertiesToLoad[2] = PropertyManager.ObjectCategory;

            ADSearcher searcher = new ADSearcher(serversEntry, filter, propertiesToLoad, SearchScope.Subtree, true /* paged search */, true /* cache results */);
            SearchResultCollection resCol = searcher.FindAll();

            try
            {
                if (resCol.Count != 2)
                {
                    throw new ActiveDirectoryOperationException(SR.Format(SR.NoHostNameOrPortNumber, dn));
                }

                foreach (SearchResult res in resCol)
                {
                    string objectCategoryValue = (string)PropertyManager.GetSearchResultPropertyValue(res, PropertyManager.ObjectCategory)!;
                    if ((objectCategoryValue.Length >= ntdsdsa.Length) && (Utils.Compare(objectCategoryValue, 0, ntdsdsa.Length, ntdsdsa, 0, ntdsdsa.Length) == 0))
                    {
                        // ntdsa object
                        ldapPort = (int)PropertyManager.GetSearchResultPropertyValue(res, PropertyManager.MsDSPortLDAP)!;
                    }
                    else
                    {
                        // server object
                        dnsHostName = (string?)PropertyManager.GetSearchResultPropertyValue(res, PropertyManager.DnsHostName);
                    }
                }
            }
            finally
            {
                resCol.Dispose();
                serversEntry.Dispose();
            }

            if ((ldapPort == -1) || (dnsHostName == null))
            {
                throw new ActiveDirectoryOperationException(SR.Format(SR.NoHostNameOrPortNumber, dn));
            }

            return dnsHostName + ":" + ldapPort;
        }

        internal static string GetAdamHostNameAndPortsFromNTDSA(DirectoryContext context, string dn)
        {
            string? dnsHostName = null;
            int ldapPort = -1;
            int sslPort = -1;
            string ntdsaDn = dn;
            string serverDn = GetPartialDN(dn, 1);
            string serversDn = GetPartialDN(dn, 2);
            string ntdsdsa = "CN=NTDS-DSA";

            DirectoryEntry serversEntry = DirectoryEntryManager.GetDirectoryEntry(context, serversDn);

            string filter = "(|(&(" + PropertyManager.ObjectCategory + "=server)(" + PropertyManager.DistinguishedName + "=" + GetEscapedFilterValue(serverDn) + "))" +
                            "(&(" + PropertyManager.ObjectCategory + "=nTDSDSA)(" + PropertyManager.DistinguishedName + "=" + GetEscapedFilterValue(ntdsaDn) + ")))";
            string[] propertiesToLoad = new string[4];
            propertiesToLoad[0] = PropertyManager.DnsHostName;
            propertiesToLoad[1] = PropertyManager.MsDSPortLDAP;
            propertiesToLoad[2] = PropertyManager.MsDSPortSSL;
            propertiesToLoad[3] = PropertyManager.ObjectCategory;

            ADSearcher searcher = new ADSearcher(serversEntry, filter, propertiesToLoad, SearchScope.Subtree, true /* paged search */, true /* cache results */);
            SearchResultCollection resCol = searcher.FindAll();

            try
            {
                if (resCol.Count != 2)
                {
                    throw new ActiveDirectoryOperationException(SR.Format(SR.NoHostNameOrPortNumber, dn));
                }

                foreach (SearchResult res in resCol)
                {
                    string objectCategoryValue = (string)PropertyManager.GetSearchResultPropertyValue(res, PropertyManager.ObjectCategory)!;
                    if ((objectCategoryValue.Length >= ntdsdsa.Length) && (Utils.Compare(objectCategoryValue, 0, ntdsdsa.Length, ntdsdsa, 0, ntdsdsa.Length) == 0))
                    {
                        // ntdsa object
                        ldapPort = (int)PropertyManager.GetSearchResultPropertyValue(res, PropertyManager.MsDSPortLDAP)!;
                        sslPort = (int)PropertyManager.GetSearchResultPropertyValue(res, PropertyManager.MsDSPortSSL)!;
                    }
                    else
                    {
                        // server object
                        dnsHostName = (string?)PropertyManager.GetSearchResultPropertyValue(res, PropertyManager.DnsHostName);
                    }
                }
            }
            finally
            {
                resCol.Dispose();
                serversEntry.Dispose();
            }

            if ((ldapPort == -1) || (sslPort == -1) || (dnsHostName == null))
            {
                throw new ActiveDirectoryOperationException(SR.Format(SR.NoHostNameOrPortNumber, dn));
            }

            return dnsHostName + ":" + ldapPort + ":" + sslPort;
        }

        //
        // If distinguished name is in the form cn=a,cn=b,.... this will return cn=a
        //
        internal static string GetRdnFromDN(string distinguishedName)
        {
            Component[] dnComponents = GetDNComponents(distinguishedName);
            // dnComponents will have atleast one component
            string rdn = dnComponents[0].Name + "=" + dnComponents[0].Value;
            return rdn;
        }

        //
        // if distinguished name is in the form of cn=a,cn=b,cn=c and startingIndex is 1, this will return cn=b,cn=c
        //
        internal static string GetPartialDN(string distinguishedName, int startingIndex)
        {
            string resultDN = "";
            Component[] dnComponents = GetDNComponents(distinguishedName);
            bool firstTime = true;
            for (int i = startingIndex; i < dnComponents.GetLength(0); i++)
            {
                if (firstTime)
                {
                    resultDN = dnComponents[i].Name + "=" + dnComponents[i].Value;
                    firstTime = false;
                }
                else
                {
                    resultDN += "," + dnComponents[i].Name + "=" + dnComponents[i].Value;
                }
            }

            return resultDN;
        }

        //
        // Splits up a DN into it's components
        // e.g. cn=a,cn=b,dc=c,dc=d would be returned as
        // a component array
        // components[0].name = cn
        // components[0].value = a
        // components[1].name = cn
        // components[1].value = b ... and so on
        //
        internal static Component[] GetDNComponents(string distinguishedName)
        {
            Debug.Assert(distinguishedName != null, "Utils.GetDNComponents: distinguishedName is null");

            // First split by ','
            string[] components = Split(distinguishedName, ',');
            Component[] dnComponents = new Component[components.GetLength(0)];

            for (int i = 0; i < components.GetLength(0); i++)
            {
                // split each component by '='
                string[] subComponents = Split(components[i], '=');
                if (subComponents.GetLength(0) != 2)
                {
                    throw new ArgumentException(SR.InvalidDNFormat, nameof(distinguishedName));
                }

                dnComponents[i].Name = subComponents[0].Trim();
                if (dnComponents[i].Name!.Length == 0)
                {
                    throw new ArgumentException(SR.InvalidDNFormat, nameof(distinguishedName));
                }

                dnComponents[i].Value = subComponents[1].Trim();
                if (dnComponents[i].Value!.Length == 0)
                {
                    throw new ArgumentException(SR.InvalidDNFormat, nameof(distinguishedName));
                }
            }
            return dnComponents;
        }

        //
        // A valid DN is one which can be split based on ',' into components and each
        // components contains two tokens separated by '='
        //
        internal static bool IsValidDNFormat(string distinguishedName)
        {
            Debug.Assert(distinguishedName != null, "Utils.GetDNComponents: distinguishedName is null");

            // First split by ','
            string[] components = Split(distinguishedName, ',');
            Component[] dnComponents = new Component[components.GetLength(0)];

            for (int i = 0; i < components.GetLength(0); i++)
            {
                // split each component by '='
                string[] subComponents = Split(components[i], '=');
                if (subComponents.GetLength(0) != 2)
                {
                    return false;
                }

                dnComponents[i].Name = subComponents[0].Trim();
                if (dnComponents[i].Name!.Length == 0)
                {
                    return false;
                }

                dnComponents[i].Value = subComponents[1].Trim();
                if (dnComponents[i].Value!.Length == 0)
                {
                    return false;
                }
            }
            return true;
        }

        //
        // this method breaks up the string into tokens based on the delimiter
        // (escaped characters are those preceded by '\' or contained in quotes and
        // such characters are not considered for a match with the delimiter)
        //
        public static string[] Split(string distinguishedName, char delim)
        {
            bool inQuotedString = false;
            char curr;
            char quote = '\"';
            char escape = '\\';
            int nextTokenStart = 0;
            ArrayList resultList = new ArrayList();
            string[] results;

            // get the actual tokens
            for (int i = 0; i < distinguishedName.Length; i++)
            {
                curr = distinguishedName[i];

                if (curr == quote)
                {
                    inQuotedString = !inQuotedString;
                }
                else if (curr == escape)
                {
                    // skip the next character (if one exists)
                    if (i < (distinguishedName.Length - 1))
                    {
                        i++;
                    }
                }
                else if ((!inQuotedString) && (curr == delim))
                {
                    // we found an unqoted character that matches the delimiter
                    // split it at the delimiter (add the tokrn that ends at this delimiter)
                    resultList.Add(distinguishedName.Substring(nextTokenStart, i - nextTokenStart));
                    nextTokenStart = i + 1;
                }

                if (i == (distinguishedName.Length - 1))
                {
                    // we've reached the end

                    // if we are still in quoted string, the format is invalid
                    if (inQuotedString)
                    {
                        throw new ArgumentException(SR.InvalidDNFormat, nameof(distinguishedName));
                    }

                    // we need to end the last token
                    resultList.Add(distinguishedName.Substring(nextTokenStart, i - nextTokenStart + 1));
                }
            }

            results = new string[resultList.Count];
            for (int i = 0; i < resultList.Count; i++)
            {
                results[i] = (string)resultList[i]!;
            }

            return results;
        }

        internal static DirectoryContext GetNewDirectoryContext(string? name, DirectoryContextType contextType, DirectoryContext? context)
        {
            return new DirectoryContext(contextType, name, context);
        }

        internal static void GetDomainAndUsername(DirectoryContext context, out string? username, out string? domain)
        {
            if ((context.UserName != null) && (context.UserName.Length > 0))
            {
                string tmpUsername = context.UserName;
                int index = -1;
                if ((index = tmpUsername.IndexOf('\\')) != -1)
                {
                    domain = tmpUsername.Substring(0, index);
                    username = tmpUsername.Substring(index + 1);
                }
                else
                {
                    username = tmpUsername;
                    domain = null;
                }
            }
            else
            {
                username = context.UserName;
                domain = null;
            }
        }

        internal static unsafe IntPtr GetAuthIdentity(DirectoryContext context, SafeLibraryHandle libHandle)
        {
            IntPtr authIdentity;
            int result = 0;

            string? username;
            string? domain;

            // split the username from the context into username and domain (if possible)
            GetDomainAndUsername(context, out username, out domain);

            // create the credentials
            // call DsMakePasswordCredentialsW

            /*DWORD DsMakePasswordCredentials(
                LPTSTR User,
                LPTSTR Domain,
                LPTSTR Password,
                RPC_AUTH_IDENTITY_HANDLE* pAuthIdentity
                );*/
            var dsMakePasswordCredentials = (delegate* unmanaged<char*, char*, char*, IntPtr*, int>)global::Interop.Kernel32.GetProcAddress(libHandle, "DsMakePasswordCredentialsW");
            if (dsMakePasswordCredentials == null)
            {
                throw ExceptionHelper.GetExceptionFromErrorCode(Marshal.GetLastPInvokeError());
            }

            fixed (char* usernamePtr = username)
            fixed (char* domainPtr = domain)
            fixed (char* passwordPtr = context.Password)
            {
                result = dsMakePasswordCredentials(usernamePtr, domainPtr, passwordPtr, &authIdentity);
            }

            if (result != 0)
            {
                throw ExceptionHelper.GetExceptionFromErrorCode(result);
            }
            return authIdentity;
        }

        internal static unsafe void FreeAuthIdentity(IntPtr authIdentity, SafeLibraryHandle libHandle)
        {
            // free the credentials object
            if (authIdentity != IntPtr.Zero)
            {
                // call DsMakePasswordCredentials
                /*VOID DsFreePasswordCredentials(
                    RPC_AUTH_IDENTITY_HANDLE AuthIdentity
                    );*/
                var dsFreePasswordCredentials = (delegate* unmanaged<IntPtr, void>)global::Interop.Kernel32.GetProcAddress(libHandle, "DsFreePasswordCredentials");
                if (dsFreePasswordCredentials == null)
                {
                    throw ExceptionHelper.GetExceptionFromErrorCode(Marshal.GetLastPInvokeError());
                }
                dsFreePasswordCredentials(authIdentity);
            }
        }

        internal static unsafe IntPtr GetDSHandle(string? domainControllerName, string? domainName, IntPtr authIdentity, SafeLibraryHandle libHandle)
        {
            int result = 0;
            IntPtr handle;

            // call DsBindWithCred
            /*DWORD DsBindWithCred(
                TCHAR* DomainController,
                TCHAR*DnsDomainName,
                RPC_AUTH_IDENTITY_HANDLE AuthIdentity,
                HANDLE*phDS
                ); */
            Debug.Assert((domainControllerName != null && domainName == null) || (domainName != null && domainControllerName == null));
            var bindWithCred = (delegate* unmanaged<char*, char*, IntPtr, IntPtr*, int>)global::Interop.Kernel32.GetProcAddress(libHandle, "DsBindWithCredW");
            if (bindWithCred == null)
            {
                throw ExceptionHelper.GetExceptionFromErrorCode(Marshal.GetLastPInvokeError());
            }

            fixed (char* domainControllerNamePtr = domainControllerName)
            fixed (char* domainNamePtr = domainName)
            {
                result = bindWithCred(domainControllerNamePtr, domainNamePtr, authIdentity, &handle);
            }
            if (result != 0)
            {
                throw ExceptionHelper.GetExceptionFromErrorCode(result, domainControllerName ?? domainName);
            }
            return handle;
        }

        internal static unsafe void FreeDSHandle(IntPtr dsHandle, SafeLibraryHandle libHandle)
        {
            // DsUnbind
            if (dsHandle != IntPtr.Zero)
            {
                // call DsUnbind
                /*DWORD DsUnBind(
                    HANDLE* phDS
                    );*/
                var dsUnBind = (delegate* unmanaged<IntPtr*, int>)global::Interop.Kernel32.GetProcAddress(libHandle, "DsUnBindW");
                if (dsUnBind == null)
                {
                    throw ExceptionHelper.GetExceptionFromErrorCode(Marshal.GetLastPInvokeError());
                }
                _ = dsUnBind(&dsHandle);
            }
        }

        internal static bool CheckCapability(DirectoryEntry rootDSE, Capability capability)
        {
            bool result = false;
            if (rootDSE != null)
            {
                if (capability == Capability.ActiveDirectory)
                {
                    foreach (string supportedCapability in rootDSE.Properties[PropertyManager.SupportedCapabilities])
                    {
                        if (string.Equals(supportedCapability, SupportedCapability.ADOid, StringComparison.OrdinalIgnoreCase))
                        {
                            result = true;
                            break;
                        }
                    }
                }
                else if (capability == Capability.ActiveDirectoryApplicationMode)
                {
                    foreach (string supportedCapability in rootDSE.Properties[PropertyManager.SupportedCapabilities])
                    {
                        if (string.Equals(supportedCapability, SupportedCapability.ADAMOid, StringComparison.OrdinalIgnoreCase))
                        {
                            result = true;
                            break;
                        }
                    }
                }
                else if (capability == Capability.ActiveDirectoryOrADAM)
                {
                    foreach (string supportedCapability in rootDSE.Properties[PropertyManager.SupportedCapabilities])
                    {
                        if (string.Equals(supportedCapability, SupportedCapability.ADAMOid, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(supportedCapability, SupportedCapability.ADOid, StringComparison.OrdinalIgnoreCase))
                        {
                            result = true;
                            break;
                        }
                    }
                }
            }
            return result;
        }

        internal static DirectoryEntry GetCrossRefEntry(DirectoryContext context, DirectoryEntry partitionsEntry, string partitionName)
        {
            // search for the crossRef that matches this one and

            // build the filter
            StringBuilder str = new StringBuilder(15);
            str.Append("(&(");
            str.Append(PropertyManager.ObjectCategory);
            str.Append("=crossRef)(");
            str.Append(PropertyManager.SystemFlags);
            str.Append(":1.2.840.113556.1.4.804:=");
            str.Append((int)SystemFlag.SystemFlagNtdsNC);
            str.Append(")(!(");
            str.Append(PropertyManager.SystemFlags);
            str.Append(":1.2.840.113556.1.4.803:=");
            str.Append((int)SystemFlag.SystemFlagNtdsDomain);
            str.Append("))(");
            str.Append(PropertyManager.NCName);
            str.Append('=');
            str.Append(Utils.GetEscapedFilterValue(partitionName));
            str.Append("))");

            string filter = str.ToString();
            string[] propertiesToLoad = new string[1];

            propertiesToLoad[0] = PropertyManager.DistinguishedName;

            ADSearcher searcher = new ADSearcher(partitionsEntry, filter, propertiesToLoad, SearchScope.OneLevel, false /*not paged search*/, false /*no cached results*/);

            SearchResult? res = null;

            try
            {
                res = searcher.FindOne();

                if (res == null)
                {
                    // should not happen
                    throw new ActiveDirectoryObjectNotFoundException(SR.AppNCNotFound, typeof(ActiveDirectoryPartition), partitionName);
                }
            }
            catch (COMException e)
            {
                throw ExceptionHelper.GetExceptionFromCOMException(context, e);
            }

            _ = (string?)PropertyManager.GetSearchResultPropertyValue(res, PropertyManager.DistinguishedName);
            return res.GetDirectoryEntry();
        }

        internal static ActiveDirectoryTransportType GetTransportTypeFromDN(string DN)
        {
            Debug.Assert(DN != null);

            string rdn = GetRdnFromDN(DN);
            Component[] component = GetDNComponents(rdn);

            Debug.Assert(component.Length == 1);

            string? transportName = component[0].Value;

            if (string.Equals(transportName, "IP", StringComparison.OrdinalIgnoreCase))
                return ActiveDirectoryTransportType.Rpc;
            else if (string.Equals(transportName, "SMTP", StringComparison.OrdinalIgnoreCase))
                return ActiveDirectoryTransportType.Smtp;
            else
            {
                string message = SR.Format(SR.UnknownTransport, transportName);
                throw new ActiveDirectoryOperationException(message);
            }
        }

        internal static string GetDNFromTransportType(ActiveDirectoryTransportType transport, DirectoryContext context)
        {
            string sitesDN = DirectoryEntryManager.ExpandWellKnownDN(context, WellKnownDN.SitesContainer);
            string transportContainerDN = "CN=Inter-Site Transports," + sitesDN;

            if (transport == ActiveDirectoryTransportType.Rpc)
            {
                return "CN=IP," + transportContainerDN;
            }
            else
            {
                return "CN=SMTP," + transportContainerDN;
            }
        }

        internal static string? GetServerNameFromInvocationID(string? serverObjectDN, Guid invocationID, DirectoryServer server)
        {
            string? originatingServerName = null;

            if (serverObjectDN == null)
            {
                // this is the win2k case, we need to get the DSA address first
                string siteName = (server is DomainController) ? ((DomainController)server).SiteObjectName : ((AdamInstance)server).SiteObjectName;
                DirectoryEntry de = DirectoryEntryManager.GetDirectoryEntry(server.Context, siteName);

                // get the string representation of the invocationID
                byte[] byteGuid = invocationID.ToByteArray();
                IntPtr ptr = (IntPtr)0;
                string? stringGuid = null;

                // encode the byte arry into binary string representation
                int hr = Interop.Activeds.ADsEncodeBinaryData(byteGuid, byteGuid.Length, ref ptr);

                if (hr == 0)
                {
                    try
                    {
                        stringGuid = Marshal.PtrToStringUni(ptr);
                    }
                    finally
                    {
                        if (ptr != (IntPtr)0)
                            Interop.Activeds.FreeADsMem(ptr);
                    }
                }
                else
                {
                    // throw exception as the call failed
                    throw ExceptionHelper.GetExceptionFromCOMException(new COMException(ExceptionHelper.GetErrorMessage(hr, true), hr));
                }

                ADSearcher adSearcher = new ADSearcher(de,
                                                           "(&(objectClass=nTDSDSA)(invocationID=" + stringGuid + "))",
                                                           ActiveDirectorySite.s_distinguishedName,
                                                           SearchScope.Subtree,
                                                           false, /* don't need paged search */
                                                           false /* don't need to cache result */);
                SearchResult? srchResult = null;

                try
                {
                    srchResult = adSearcher.FindOne();
                    if (srchResult != null)
                    {
                        DirectoryEntry srvEntry = srchResult.GetDirectoryEntry().Parent;
                        originatingServerName = (string?)PropertyManager.GetPropertyValue(server.Context, srvEntry, PropertyManager.DnsHostName);
                    }
                }
                catch (COMException e)
                {
                    throw ExceptionHelper.GetExceptionFromCOMException(server.Context, e);
                }
            }
            else
            {
                DirectoryEntry de = DirectoryEntryManager.GetDirectoryEntry(server.Context, serverObjectDN);

                try
                {
                    originatingServerName = (string?)PropertyManager.GetPropertyValue(de.Parent, PropertyManager.DnsHostName);
                }
                catch (COMException e)
                {
                    if (e.ErrorCode == unchecked((int)0x80072030))
                        return null;
                    else
                        throw ExceptionHelper.GetExceptionFromCOMException(server.Context, e);
                }
                if (server is AdamInstance)
                {
                    // we might need to add the port number
                    int portnumber = (int)PropertyManager.GetPropertyValue(server.Context, de, PropertyManager.MsDSPortLDAP)!;

                    if (portnumber != 389)
                        originatingServerName = originatingServerName + ":" + portnumber;
                }
            }

            return originatingServerName;
        }

        internal static int GetRandomIndex(int count)
        {
            Random random = new Random();
            int randomNumber = random.Next();
            return (randomNumber % count);
        }

        internal static bool Impersonate(DirectoryContext context)
        {
            IntPtr hToken = (IntPtr)0;

            // default credential is specified, no need to do impersonation
            if ((context.UserName == null) && (context.Password == null))
                return false;

            string? userName;
            string? domainName;

            Utils.GetDomainAndUsername(context, out userName, out domainName);

            int result = global::Interop.Advapi32.LogonUser(userName!, domainName, context.Password, LOGON32_LOGON_NEW_CREDENTIALS, LOGON32_PROVIDER_WINNT50, ref hToken);
            // check the result
            if (result == 0)
                throw ExceptionHelper.GetExceptionFromErrorCode(Marshal.GetLastPInvokeError());

            try
            {
                result = global::Interop.Advapi32.ImpersonateLoggedOnUser(hToken);
                if (result == 0)
                {
                    result = Marshal.GetLastPInvokeError();
                    throw ExceptionHelper.GetExceptionFromErrorCode(result);
                }
            }
            finally
            {
                if (hToken != (IntPtr)0)
                    global::Interop.Kernel32.CloseHandle(hToken);
            }

            return true;
        }

        internal static void ImpersonateAnonymous()
        {
            IntPtr hThread = Interop.Kernel32.OpenThread(Interop.Kernel32.THREAD_ALL_ACCESS, false, global::Interop.Kernel32.GetCurrentThreadId());
            if (hThread == (IntPtr)0)
                throw ExceptionHelper.GetExceptionFromErrorCode(Marshal.GetLastPInvokeError());

            try
            {
                bool success = Interop.Advapi32.ImpersonateAnonymousToken(hThread);
                if (!success)
                    throw ExceptionHelper.GetExceptionFromErrorCode(Marshal.GetLastPInvokeError());
            }
            finally
            {
                if (hThread != (IntPtr)0)
                    global::Interop.Kernel32.CloseHandle(hThread);
            }
        }

        internal static void Revert()
        {
            if (!global::Interop.Advapi32.RevertToSelf())
            {
                throw ExceptionHelper.GetExceptionFromErrorCode(Marshal.GetLastPInvokeError());
            }
        }

        internal static string GetPolicyServerName(DirectoryContext context, bool isForest, bool needPdc, string? source)
        {
            string? serverName = null;
            PrivateLocatorFlags flag = PrivateLocatorFlags.DirectoryServicesRequired;

            // passes in either domain or forest name, just find the dc
            if (context.isDomain())
            {
                if (needPdc)
                {
                    flag |= PrivateLocatorFlags.PdcRequired;
                }
                serverName = Locator.GetDomainControllerInfo(null, source, null, (long)flag).DomainControllerName.Substring(2);
            }
            else
            {
                // user could pass in non-root domain server name in the context, so need to find a dc in root domain
                if (isForest)
                {
                    if (needPdc)
                    {
                        flag |= PrivateLocatorFlags.PdcRequired;
                        serverName = Locator.GetDomainControllerInfo(null, source, null, (long)flag).DomainControllerName.Substring(2);
                    }
                    else
                    {
                        if (context.ContextType == DirectoryContextType.DirectoryServer)
                        {
                            // need first to decide whether this is a server in the root domain or not
                            DirectoryEntry de = DirectoryEntryManager.GetDirectoryEntry(context, WellKnownDN.RootDSE);
                            string? namingContext = (string?)PropertyManager.GetPropertyValue(context, de, PropertyManager.DefaultNamingContext);
                            string? rootNamingContext = (string?)PropertyManager.GetPropertyValue(context, de, PropertyManager.RootDomainNamingContext);
                            if (Compare(namingContext, rootNamingContext) == 0)
                            {
                                serverName = context.Name!;
                            }
                            else
                            {
                                // it is not a server in the root domain, so we need to do dc location
                                serverName = Locator.GetDomainControllerInfo(null, source, null, (long)flag).DomainControllerName.Substring(2);
                            }
                        }
                        else
                        {
                            serverName = Locator.GetDomainControllerInfo(null, source, null, (long)flag).DomainControllerName.Substring(2);
                        }
                    }
                }
                else
                {
                    serverName = context.Name!;
                }
            }

            return serverName;
        }

        internal static SafeLsaPolicyHandle GetPolicyHandle(string serverName)
        {
            SafeLsaPolicyHandle handle;
            global::Interop.OBJECT_ATTRIBUTES objectAttribute = default;

            uint result = global::Interop.Advapi32.LsaOpenPolicy(serverName, ref objectAttribute, (int)global::Interop.Advapi32.PolicyRights.POLICY_VIEW_LOCAL_INFORMATION, out handle);
            if (result != 0)
            {
                throw ExceptionHelper.GetExceptionFromErrorCode((int)global::Interop.Advapi32.LsaNtStatusToWinError(result), serverName);
            }

            return handle;
        }

        //
        // This function returns a hashtable, where key = propertyname (lowercase) and value = ArrayList of values for that property
        // (It always searches for one object matching the searching criteria and returns the values for the specified properties using
        //  range retrieval)
        //
        internal static Hashtable GetValuesWithRangeRetrieval(DirectoryEntry searchRootEntry, string? filter, ArrayList propertiesToLoad, SearchScope searchScope)
        {
            return GetValuesWithRangeRetrieval(searchRootEntry, filter, propertiesToLoad, new ArrayList(), searchScope);
        }

        //
        // This function returns a hashtable, where key = propertyname (lowercase) and value = ArrayList of values for that property
        // (It always searches for one object matching the searching criteria and returns the values for the specified properties using
        //  range retrieval)
        //
        internal static Hashtable GetValuesWithRangeRetrieval(DirectoryEntry searchRootEntry, string? filter, ArrayList propertiesWithRangeRetrieval, ArrayList propertiesWithoutRangeRetrieval, SearchScope searchScope)
        {
            ADSearcher searcher = new ADSearcher(searchRootEntry, filter, Array.Empty<string>(), searchScope, false /* paged search */, false /* cache results */);
            SearchResult? res = null;
            int rangeStart = 0;
            Hashtable results = new Hashtable();
            Hashtable propertyNamesWithRangeInfo = new Hashtable();
            ArrayList propertyNamesWithoutRangeInfo = new ArrayList();
            ArrayList propertiesStillToLoad = new ArrayList();

            //
            // The logic is as follows:
            // For each property in the propertiesWithRangeRetrieval we add the range as 0-*, e.g. member would be "member;range=0-*"
            // When the results are returned if the property name is not present or is still "member;range=0-*", then we got the last batch and so we
            // will not retrieve this property in the next round. However, if the property comes back as "member;range=0-1499" this means
            // we still have more values to retrieve, so we will retrieve "member;range=5000-*" next time and so on...
            //
            // Properties in the propertiesWithoutRangeRetrieval arraylist, we only include the properties in the first search without any range info
            //

            foreach (string propertyName in propertiesWithoutRangeRetrieval)
            {
                // need to convert to lower case since S.DS returns property names in all lower case
                string lowerCasePropertyName = propertyName.ToLowerInvariant();
                propertyNamesWithoutRangeInfo.Add(lowerCasePropertyName);
                results.Add(lowerCasePropertyName, new ArrayList());
                // add to the seachers's propertiesToLoad
                searcher.PropertiesToLoad.Add(propertyName);
            }

            // keep a list of properties for which we have not yet retrieved all the
            // results
            foreach (string propertyName in propertiesWithRangeRetrieval)
            {
                // need to convert to lower case since S.DS returns property names in all lower case
                string lowerCasePropertyName = propertyName.ToLowerInvariant();
                propertiesStillToLoad.Add(lowerCasePropertyName);
                results.Add(lowerCasePropertyName, new ArrayList());
            }

            do
            {
                foreach (string propertyName in propertiesStillToLoad)
                {
                    string propertyToLoad = propertyName + ";range=" + rangeStart + "-*";
                    searcher.PropertiesToLoad.Add(propertyToLoad);
                    // need to convert to lower case since S.DS returns property names in all lower case
                    propertyNamesWithRangeInfo.Add(propertyName.ToLowerInvariant(), propertyToLoad);
                }

                //clear for the nezxt round
                propertiesStillToLoad.Clear();

                res = searcher.FindOne();
                if (res != null)
                {
                    foreach (string propertyNameWithRangeInfo in res.Properties.PropertyNames)
                    {
                        int index = propertyNameWithRangeInfo.IndexOf(';');

                        string? propertyName = null;
                        if (index != -1)
                        {
                            propertyName = propertyNameWithRangeInfo.Substring(0, index);
                        }
                        else
                        {
                            propertyName = propertyNameWithRangeInfo;
                        }

                        if (!propertyNamesWithRangeInfo.Contains(propertyName) && !propertyNamesWithoutRangeInfo.Contains(propertyName))
                        {
                            // we're not interested in this property (could be adspath), so just skip
                            continue;
                        }

                        ArrayList values = (ArrayList)results[propertyName]!;
                        values.AddRange(res.Properties[propertyNameWithRangeInfo]);

                        if (propertyNamesWithRangeInfo.Contains(propertyName))
                        {
                            //
                            // if this is a property retrieved along with range retrieval, check if we need to include
                            // it in the next round.
                            //

                            string propertyToLoad = (string)propertyNamesWithRangeInfo[propertyName]!;

                            if ((propertyNameWithRangeInfo.Length >= propertyToLoad.Length) && (Utils.Compare(propertyToLoad, 0, propertyToLoad.Length, propertyNameWithRangeInfo, 0, propertyToLoad.Length) != 0))
                            {
                                propertiesStillToLoad.Add(propertyName);
                                rangeStart += res.Properties[propertyNameWithRangeInfo].Count;
                            }
                        }
                    }
                }
                else
                {
                    throw new ActiveDirectoryObjectNotFoundException(SR.DSNotFound);
                }

                // clear for the next round
                searcher.PropertiesToLoad.Clear();
                propertyNamesWithRangeInfo.Clear();
            } while (propertiesStillToLoad.Count > 0);

            return results;
        }

        internal static ArrayList GetReplicaList(DirectoryContext context, string? partitionName, string? siteName, bool isDefaultNC, bool isADAM, bool isGC)
        {
            ArrayList ntdsaNames = new ArrayList();
            ArrayList dnsNames = new ArrayList();

            //
            // The algorithm is as follows:
            // 1. Search for the crossRef entry of this partition and retrieve the msDS-NC-Replica-Locations  and
            //     msDS_NC-RO-Replica-Locations for a list of the replicas (using range retrieval). This is needed
            //     in the case of application partition only.
            // 2. Search for the ntdsa objects of these replicas which have the partition in the Has-MasterNCs attribute (if partition name is specified
            //     else search for all ntdsa objects)
            // 3. For each replica in the resulting set, check if the msDS-Has-InstantiatedNCs attribute is of the form B:8:00000005:<DN of partition>
            //     where the second nibble from the least significant side is 0, B:8:00000015 would signify that the partition is still being replicated in
            //     and B:8:00000025 would indicate the partition is being replicated out (replica deletion) (again, this is only if partitionName is specified).
            //     This step is needed only for application partitions. This will be ignored for read-only NCs as it will ONLY be populated locally to each RODC.
            //

            Hashtable serverNames = new Hashtable();
            Hashtable serverPorts = new Hashtable();
            StringBuilder ntdsaFilter = new StringBuilder(10);
            StringBuilder serverFilter = new StringBuilder(10);
            StringBuilder roNtdsaFilter = new StringBuilder(10);
            StringBuilder roServerFilter = new StringBuilder(10);

            bool useReplicaInfo = false;
            string? configurationNamingContext = null;

            try
            {
                configurationNamingContext = DirectoryEntryManager.ExpandWellKnownDN(context, WellKnownDN.ConfigurationNamingContext);
            }
            catch (COMException e)
            {
                throw ExceptionHelper.GetExceptionFromCOMException(context, e);
            }

            //
            // If partition name is not null and is not Configuration/Schema/defaultNC , we need to get the list of the
            // msDS-NC-Replica-Locations and msDS-NC-RO-Replica-Locations (for Configuration/Schema, these attributes are
            // not populated, so we just return a list of all the servers)
            //
            if (partitionName != null && !isDefaultNC)
            {
                DistinguishedName dn = new DistinguishedName(partitionName);
                DistinguishedName configDn = new DistinguishedName(configurationNamingContext);
                DistinguishedName schemaDn = new DistinguishedName("CN=Schema," + configurationNamingContext);

                if ((!(configDn.Equals(dn))) && (!(schemaDn.Equals(dn))))
                {
                    useReplicaInfo = true;
                }
            }

            if (useReplicaInfo)
            {
                DirectoryEntry? partitionsEntry = null;
                DirectoryEntry? fsmoPartitionsEntry = null;

                try
                {
                    //
                    // get the partitions entry on the naming master
                    //
                    partitionsEntry = DirectoryEntryManager.GetDirectoryEntry(context, "CN=Partitions," + configurationNamingContext);
                    string? fsmoRoleOwnerName = null;
                    if (isADAM)
                    {
                        fsmoRoleOwnerName = Utils.GetAdamDnsHostNameFromNTDSA(context, (string)PropertyManager.GetPropertyValue(context, partitionsEntry, PropertyManager.FsmoRoleOwner)!);
                    }
                    else
                    {
                        fsmoRoleOwnerName = Utils.GetDnsHostNameFromNTDSA(context, (string)PropertyManager.GetPropertyValue(context, partitionsEntry, PropertyManager.FsmoRoleOwner)!);
                    }

                    DirectoryContext fsmoContext = Utils.GetNewDirectoryContext(fsmoRoleOwnerName, DirectoryContextType.DirectoryServer, context);
                    fsmoPartitionsEntry = DirectoryEntryManager.GetDirectoryEntry(fsmoContext, "CN=Partitions," + configurationNamingContext);

                    // get the properties using range retrieval
                    // (since msDS-NC-Replica-Locations and msDS-NC-RO-Replica-Locations are multi-valued)
                    string filter = "(&(" + PropertyManager.ObjectCategory + "=crossRef)(" + PropertyManager.NCName + "=" + Utils.GetEscapedFilterValue(partitionName!) + "))";
                    ArrayList propertyNames = new ArrayList();
                    propertyNames.Add(PropertyManager.MsDSNCReplicaLocations);
                    propertyNames.Add(PropertyManager.MsDSNCROReplicaLocations);

                    Hashtable? values = null;
                    try
                    {
                        values = Utils.GetValuesWithRangeRetrieval(fsmoPartitionsEntry, filter, propertyNames, SearchScope.OneLevel);
                    }
                    catch (COMException e)
                    {
                        throw ExceptionHelper.GetExceptionFromCOMException(fsmoContext, e);
                    }
                    catch (ActiveDirectoryObjectNotFoundException)
                    {
                        // this means that this partition does not exist, so we return an empty collection
                        return dnsNames;
                    }

                    // extract the property values
                    ArrayList replicaLocations = (ArrayList)values[PropertyManager.MsDSNCReplicaLocations.ToLowerInvariant()]!;
                    ArrayList roReplicaLocations = (ArrayList)values[PropertyManager.MsDSNCROReplicaLocations.ToLowerInvariant()]!;
                    Debug.Assert(replicaLocations != null);

                    if (replicaLocations.Count == 0)
                    {
                        // At this point we find that there are no replica locations, so we return an empty collection.
                        return dnsNames;
                    }

                    foreach (string replicaLocation in replicaLocations)
                    {
                        ntdsaFilter.Append('(');
                        ntdsaFilter.Append(PropertyManager.DistinguishedName);
                        ntdsaFilter.Append('=');
                        ntdsaFilter.Append(Utils.GetEscapedFilterValue(replicaLocation));
                        ntdsaFilter.Append(')');

                        serverFilter.Append('(');
                        serverFilter.Append(PropertyManager.DistinguishedName);
                        serverFilter.Append('=');
                        serverFilter.Append(Utils.GetEscapedFilterValue(Utils.GetPartialDN(replicaLocation, 1)));
                        serverFilter.Append(')');
                    }

                    foreach (string roReplicaLocation in roReplicaLocations)
                    {
                        roNtdsaFilter.Append('(');
                        roNtdsaFilter.Append(PropertyManager.DistinguishedName);
                        roNtdsaFilter.Append('=');
                        roNtdsaFilter.Append(Utils.GetEscapedFilterValue(roReplicaLocation));
                        roNtdsaFilter.Append(')');

                        roServerFilter.Append('(');
                        roServerFilter.Append(PropertyManager.DistinguishedName);
                        roServerFilter.Append('=');
                        roServerFilter.Append(Utils.GetEscapedFilterValue(Utils.GetPartialDN(roReplicaLocation, 1)));
                        roServerFilter.Append(')');
                    }
                }
                catch (COMException e)
                {
                    throw ExceptionHelper.GetExceptionFromCOMException(context, e);
                }
                finally
                {
                    partitionsEntry?.Dispose();
                    fsmoPartitionsEntry?.Dispose();
                }
            }

            string? searchRootDN = null;
            DirectoryEntry? searchRootEntry = null;
            try
            {
                // check whether we can narrow down our search within a specific site
                if (siteName != null)
                {
                    searchRootDN = "CN=Servers,CN=" + siteName + ",CN=Sites," + configurationNamingContext;
                }
                else
                {
                    searchRootDN = "CN=Sites," + configurationNamingContext;
                }
                searchRootEntry = DirectoryEntryManager.GetDirectoryEntry(context, searchRootDN);

                // set up searcher object
                string? filter2 = null;
                if (ntdsaFilter.ToString().Length == 0)
                {
                    // either this is the case when we want all the servers (partitionName = null or partitionName is Configuration/Schema)
                    // or this is the case when partitionName is the defaultNamingContext
                    // for the latter we want to restrict the search to only that naming context

                    if (isDefaultNC)
                    {
                        Debug.Assert(partitionName != null);
                        Debug.Assert(!isGC);

                        filter2 = "(|(&(" + PropertyManager.ObjectCategory + "=nTDSDSA)(" + PropertyManager.HasMasterNCs +
                            "=" + Utils.GetEscapedFilterValue(partitionName) + "))(&(" + PropertyManager.ObjectCategory + "=nTDSDSARO)(" +
                            PropertyManager.MsDSHasFullReplicaNCs + "=" + Utils.GetEscapedFilterValue(partitionName) + "))(" +
                            PropertyManager.ObjectCategory + "=server))";
                    }
                    else
                    {
                        if (isGC)
                        {
                            filter2 = "(|(&(" + PropertyManager.ObjectCategory + "=nTDSDSA)(" +
                                PropertyManager.Options + ":1.2.840.113556.1.4.804:=1))(&(" +
                                PropertyManager.ObjectCategory + "=nTDSDSARO)(" +
                                PropertyManager.Options + ":1.2.840.113556.1.4.804:=1))(" +
                                PropertyManager.ObjectCategory + "=server))";
                        }
                        else
                        {
                            filter2 = "(|" + "(" + PropertyManager.ObjectCategory + "=nTDSDSA)(" +
                                PropertyManager.ObjectCategory + "=nTDSDSARO)(" +
                                PropertyManager.ObjectCategory + "=server))";
                        }
                    }
                }
                else
                {
                    Debug.Assert(partitionName != null);
                    // resctrict the search to the servers that were listed in the crossRef
                    if (isGC)
                    {
                        if (roNtdsaFilter.Length > 0)
                        {
                            //for read-only NCs, msDS-hasFullReplicaNCs is equivalent of msDS-hasMasterNCs. But since msDS-hasFullReplicaNCs will be
                            //populated ONLY on each RODC, it can't be used. Since roNtdsaFilter is populated using input partitionName, we should
                            //be fine.
                            filter2 = "(|(&(" + PropertyManager.ObjectCategory + "=nTDSDSA)(" + PropertyManager.Options +
                                ":1.2.840.113556.1.4.804:=1)(" + PropertyManager.MsDSHasMasterNCs + "=" + Utils.GetEscapedFilterValue(partitionName) +
                                ")(|" + ntdsaFilter.ToString() + "))" + "(&(" + PropertyManager.ObjectCategory + "=nTDSDSARO)(" + PropertyManager.Options +
                                ":1.2.840.113556.1.4.804:=1)(|" + roNtdsaFilter.ToString() + "))" +
                                "(&(" + PropertyManager.ObjectCategory + "=server)(|" + serverFilter.ToString() + "))" +
                                "(&(" + PropertyManager.ObjectCategory + "=server)(|" + roServerFilter.ToString() + ")))";
                        }
                        else
                        {
                            filter2 = "(|(&(" + PropertyManager.ObjectCategory + "=nTDSDSA)(" + PropertyManager.Options +
                                ":1.2.840.113556.1.4.804:=1)(" + PropertyManager.MsDSHasMasterNCs + "=" + Utils.GetEscapedFilterValue(partitionName) +
                                ")(|" + ntdsaFilter.ToString() + "))" + "(&(" + PropertyManager.ObjectCategory + "=server)(|" + serverFilter.ToString() + ")))";
                        }
                    }
                    else
                    {
                        if (roNtdsaFilter.Length > 0)
                        {
                            //for read-only NCs, msDS-hasFullReplicaNCs is equivalent of msDS-hasMasterNCs. But since msDS-hasFullReplicaNCs will be
                            //populated ONLY on each RODC, it can't be used. Since roNtdsaFilter is populated using input partitionName, we should
                            //be fine.
                            filter2 = "(|(&(" + PropertyManager.ObjectCategory + "=nTDSDSA)(" + PropertyManager.MsDSHasMasterNCs + "=" + Utils.GetEscapedFilterValue(partitionName) + ")(|" + ntdsaFilter.ToString() + "))"
                                + "(&(" + PropertyManager.ObjectCategory + "=nTDSDSARO)(|" + roNtdsaFilter.ToString() + "))"
                                + "(&(" + PropertyManager.ObjectCategory + "=server)(|" + serverFilter.ToString() + "))"
                                + "(&(" + PropertyManager.ObjectCategory + "=server)(|" + roServerFilter.ToString() + ")))";
                        }
                        else
                        {
                            filter2 = "(|(&(" + PropertyManager.ObjectCategory + "=nTDSDSA)(" + PropertyManager.MsDSHasMasterNCs + "=" + Utils.GetEscapedFilterValue(partitionName) + ")(|" + ntdsaFilter.ToString() + "))"
                                + "(&(" + PropertyManager.ObjectCategory + "=server)(|" + serverFilter.ToString() + ")))";
                        }
                    }
                }

                ADSearcher searcher2 = new ADSearcher(searchRootEntry, filter2, Array.Empty<string>(), SearchScope.Subtree);
                SearchResultCollection? resCol = null;
                bool needToContinueRangeRetrieval = false;
                ArrayList ntdsaNamesForRangeRetrieval = new ArrayList();
                int rangeStart = 0;

                string propertyWithRangeInfo = PropertyManager.MsDSHasInstantiatedNCs + ";range=0-*";
                searcher2.PropertiesToLoad.Add(PropertyManager.DistinguishedName);
                searcher2.PropertiesToLoad.Add(PropertyManager.DnsHostName);
                searcher2.PropertiesToLoad.Add(propertyWithRangeInfo);
                searcher2.PropertiesToLoad.Add(PropertyManager.ObjectCategory);
                if (isADAM)
                {
                    searcher2.PropertiesToLoad.Add(PropertyManager.MsDSPortLDAP);
                }

                try
                {
                    string objectCategoryValue = "CN=NTDS-DSA";
                    string roObjectCategoryValue = "CN=NTDS-DSA-RO";

                    resCol = searcher2.FindAll();

                    try
                    {
                        foreach (SearchResult res in resCol)
                        {
                            string objectCategory = (string)PropertyManager.GetSearchResultPropertyValue(res, PropertyManager.ObjectCategory)!;
                            if ((objectCategory.Length >= objectCategoryValue.Length) && (Utils.Compare(objectCategory, 0, objectCategoryValue.Length, objectCategoryValue, 0, objectCategoryValue.Length) == 0))
                            {
                                //
                                // ntdsa objects (return only those servers which have the partition fully instantiated)
                                //
                                string ntdsaName = (string)PropertyManager.GetSearchResultPropertyValue(res, PropertyManager.DistinguishedName)!;
                                if (useReplicaInfo)
                                {
                                    if ((objectCategory.Length >= roObjectCategoryValue.Length) && (Utils.Compare(objectCategory, 0, roObjectCategoryValue.Length, roObjectCategoryValue, 0, roObjectCategoryValue.Length) == 0))
                                    {
                                        //for read-only NCs, msDS-HasInstantiatedNCs will be populated ONLY on each RODC and it will NOT be
                                        //replicated to other DCs. So it can't be used, provided we connect to each RODC and verify it which is not
                                        //really required as msDS-NC-RO-Replica-Locations should provide the correct information.
                                        ntdsaNames.Add(ntdsaName);
                                        if (isADAM)
                                        {
                                            serverPorts.Add(ntdsaName, (int)PropertyManager.GetSearchResultPropertyValue(res, PropertyManager.MsDSPortLDAP)!);
                                        }
                                        continue;
                                    }

                                    // Here we need to check if we retrieved all the msDS-HasInstantiatedNCs values
                                    // if not we need to continue with the range retrieval (in parallel for the various ntdsa objects)

                                    string? propertyName = null;
                                    if (!res.Properties.Contains(propertyWithRangeInfo))
                                    {
                                        // find the property name with the range info
                                        foreach (string property in res.Properties.PropertyNames)
                                        {
                                            if ((property.Length >= PropertyManager.MsDSHasInstantiatedNCs.Length) && (Utils.Compare(property, 0, PropertyManager.MsDSHasInstantiatedNCs.Length, PropertyManager.MsDSHasInstantiatedNCs, 0, PropertyManager.MsDSHasInstantiatedNCs.Length) == 0))
                                            {
                                                propertyName = property;
                                                break;
                                            }
                                        }
                                    }
                                    else
                                    {
                                        propertyName = propertyWithRangeInfo;
                                    }

                                    if (propertyName == null)
                                    {
                                        // property does not exist, possiblyno values, so continue
                                        continue;
                                    }

                                    bool foundPartitionEntry = false;
                                    int valueCount = 0;

                                    foreach (string dnString in res.Properties[propertyName])
                                    {
                                        Debug.Assert(dnString.Length > 10, "ConfigurationSet::GetReplicaList - dnWithBinary is not in the expected format.");

                                        if (((dnString.Length - 13) >= partitionName!.Length) && (Utils.Compare(dnString, 13, partitionName.Length, partitionName, 0, partitionName.Length) == 0))
                                        {
                                            // found the entry that corresponds to this partition so even if we didn't get all the values of the
                                            // multivalues attribute we can stop here.
                                            foundPartitionEntry = true;

                                            if (string.Compare(dnString, 10, "0", 0, 1, StringComparison.OrdinalIgnoreCase) == 0)
                                            {
                                                // this server has the partition fully instantiated
                                                ntdsaNames.Add(ntdsaName);
                                                if (isADAM)
                                                {
                                                    serverPorts.Add(ntdsaName, (int)PropertyManager.GetSearchResultPropertyValue(res, PropertyManager.MsDSPortLDAP)!);
                                                }
                                                break;
                                            }
                                        }

                                        valueCount++;
                                    }

                                    if ((!foundPartitionEntry) && ((propertyName.Length >= propertyWithRangeInfo.Length) && (Utils.Compare(propertyName, 0, propertyWithRangeInfo.Length, propertyWithRangeInfo, 0, propertyWithRangeInfo.Length) != 0)))
                                    {
                                        needToContinueRangeRetrieval = true;
                                        ntdsaNamesForRangeRetrieval.Add(ntdsaName);
                                        rangeStart = valueCount;
                                    }
                                }
                                else
                                {
                                    // schema or configuration partition, so we add all the servers
                                    ntdsaNames.Add(ntdsaName);
                                    if (isADAM)
                                    {
                                        serverPorts.Add(ntdsaName, (int)PropertyManager.GetSearchResultPropertyValue(res, PropertyManager.MsDSPortLDAP)!);
                                    }
                                }
                            }
                            else
                            {
                                // server objects, just keep infor regarding the dns name (to be used later), if not available we will throw an error later
                                // when we try to retrieve this info for a valid DC/GC
                                if (res.Properties.Contains(PropertyManager.DnsHostName))
                                {
                                    serverNames.Add("CN=NTDS Settings," + (string)PropertyManager.GetSearchResultPropertyValue(res, PropertyManager.DistinguishedName)!,
                                                (string?)PropertyManager.GetSearchResultPropertyValue(res, PropertyManager.DnsHostName));
                                }
                            }
                        }
                    }
                    finally
                    {
                        resCol?.Dispose();
                    }

                    if (needToContinueRangeRetrieval)
                    {
                        StringBuilder str = new StringBuilder(20);
                        // Now continue with range retrieval if necessary for msDS-HasInstantiatedNCs
                        do
                        {
                            // Here we only need the NTDS settings objects of the ntdsaNames that need range retrieval

                            // this should be greater than 0, since needToContinueRangeRetrieval is true
                            Debug.Assert(ntdsaNamesForRangeRetrieval.Count > 0);

                            str.Clear();
                            if (ntdsaNamesForRangeRetrieval.Count > 1)
                            {
                                str.Append("(|");
                            }

                            foreach (string name in ntdsaNamesForRangeRetrieval)
                            {
                                str.Append('(');
                                str.Append(PropertyManager.NCName);
                                str.Append('=');
                                str.Append(Utils.GetEscapedFilterValue(name));
                                str.Append(')');
                            }

                            if (ntdsaNamesForRangeRetrieval.Count > 1)
                            {
                                str.Append(')');
                            }

                            // Clear it for the next round of range retrieval
                            ntdsaNamesForRangeRetrieval.Clear();
                            needToContinueRangeRetrieval = false;

                            searcher2.Filter = "(&" + "(" + PropertyManager.ObjectCategory + "=nTDSDSA)" + str.ToString() + ")";

                            string propertyWithRangeInfo2 = PropertyManager.MsDSHasInstantiatedNCs + ";range=" + rangeStart + "-*";
                            searcher2.PropertiesToLoad.Clear();
                            searcher2.PropertiesToLoad.Add(propertyWithRangeInfo2);
                            searcher2.PropertiesToLoad.Add(PropertyManager.DistinguishedName);

                            SearchResultCollection resCol2 = searcher2.FindAll();

                            try
                            {
                                foreach (SearchResult res in resCol2)
                                {
                                    string ntdsaName = (string)PropertyManager.GetSearchResultPropertyValue(res, PropertyManager.DistinguishedName)!;
                                    // Here we need to check if we retrieved all the msDS-HasInstantiatedNCs values
                                    // if not we need to continue with the range retrieval (in parallel for the various ntdsa objects)
                                    string? propertyName = null;
                                    if (!res.Properties.Contains(propertyWithRangeInfo2))
                                    {
                                        // find the property name with the range info
                                        foreach (string property in res.Properties.PropertyNames)
                                        {
                                            if (string.Compare(property, 0, PropertyManager.MsDSHasInstantiatedNCs, 0, PropertyManager.MsDSHasInstantiatedNCs.Length, StringComparison.OrdinalIgnoreCase) == 0)
                                            {
                                                propertyName = property;
                                                break;
                                            }
                                        }
                                    }
                                    else
                                    {
                                        propertyName = propertyWithRangeInfo2;
                                    }

                                    if (propertyName == null)
                                    {
                                        // property does not exist, possiblyno values, so continue
                                        continue;
                                    }

                                    bool foundPartitionEntry = false;
                                    int valueCount = 0;

                                    foreach (string dnString in res.Properties[propertyName])
                                    {
                                        Debug.Assert(dnString.Length > 10, "ConfigurationSet::GetReplicaList - dnWithBinary is not in the expected format.");

                                        if (((dnString.Length - 13) >= partitionName!.Length) && (Utils.Compare(dnString, 13, partitionName.Length, partitionName, 0, partitionName.Length) == 0))
                                        {
                                            foundPartitionEntry = true;

                                            if (string.Compare(dnString, 10, "0", 0, 1, StringComparison.OrdinalIgnoreCase) == 0)
                                            {
                                                ntdsaNames.Add(ntdsaName);
                                                if (isADAM)
                                                {
                                                    serverPorts.Add(ntdsaName, (int)PropertyManager.GetSearchResultPropertyValue(res, PropertyManager.MsDSPortLDAP)!);
                                                }
                                                break;
                                            }
                                        }

                                        valueCount++;
                                    }

                                    if ((!foundPartitionEntry) && ((propertyName.Length >= propertyWithRangeInfo2.Length) && (Utils.Compare(propertyName, 0, propertyWithRangeInfo2.Length, propertyWithRangeInfo2, 0, propertyWithRangeInfo2.Length) != 0)))
                                    {
                                        needToContinueRangeRetrieval = true;
                                        ntdsaNamesForRangeRetrieval.Add(ntdsaName);
                                        rangeStart += valueCount;
                                    }
                                }
                            }
                            finally
                            {
                                resCol2.Dispose();
                            }
                        } while (needToContinueRangeRetrieval);
                    }
                }
                catch (COMException e)
                {
                    if (e.ErrorCode == unchecked((int)0x80072030) && siteName != null)
                    {
                        // this means that the site object does not exist, so we return an empty collection
                        return dnsNames;
                    }
                    else
                    {
                        throw ExceptionHelper.GetExceptionFromCOMException(context, e);
                    }
                }
            }
            finally
            {
                searchRootEntry?.Dispose();
            }

            // convert the ntdsa object names to server:port
            foreach (string ntdsaName in ntdsaNames)
            {
                string? hostName = (string?)serverNames[ntdsaName];

                if (hostName == null)
                {
                    Debug.Fail($"ConfigurationSet::GetReplicaList - no dnsHostName information for replica {ntdsaName}");
                    if (isADAM)
                    {
                        throw new ActiveDirectoryOperationException(SR.Format(SR.NoHostNameOrPortNumber, ntdsaName));
                    }
                    else
                    {
                        throw new ActiveDirectoryOperationException(SR.Format(SR.NoHostName, ntdsaName));
                    }
                }

                if (isADAM)
                {
                    if (serverPorts[ntdsaName] == null)
                    {
                        Debug.Fail($"ConfigurationSet::GetReplicaList - no port number  information for replica {ntdsaName}");
                        throw new ActiveDirectoryOperationException(SR.Format(SR.NoHostNameOrPortNumber, ntdsaName));
                    }
                }

                if (isADAM)
                {
                    dnsNames.Add(hostName + ":" + (int)serverPorts[ntdsaName]!);
                }
                else
                {
                    dnsNames.Add(hostName);
                }
            }

            return dnsNames;
        }

        //
        // Generates an escaped name that may be used in an LDAP query. The characters
        // ( ) * \ must be escaped when used in an LDAP query per RFC 2254.
        //
        internal static string GetEscapedFilterValue(string filterValue)
        {
            int index = -1;
            char[] specialCharacters = new char[] { '(', ')', '*', '\\' };

            index = filterValue.IndexOfAny(specialCharacters);
            if (index != -1)
            {
                //
                // if it contains any of the special characters then we
                // need to escape those
                //

                StringBuilder str = new StringBuilder(2 * filterValue.Length);

                str.Append(filterValue.Substring(0, index));

                for (int i = index; i < filterValue.Length; i++)
                {
                    switch (filterValue[i])
                    {
                        case ('('):
                            {
                                str.Append("\\28");
                                break;
                            }

                        case (')'):
                            {
                                str.Append("\\29");
                                break;
                            }

                        case ('*'):
                            {
                                str.Append("\\2A");
                                break;
                            }

                        case ('\\'):
                            {
                                str.Append("\\5C");
                                break;
                            }

                        default:
                            {
                                str.Append(filterValue[i]);
                                break;
                            }
                    }
                }

                return str.ToString();
            }
            else
            {
                //
                // just return the original string
                //

                return filterValue;
            }
        }

        internal static string GetEscapedPath(string originalPath)
        {
            NativeComInterfaces.IAdsPathname pathCracker = (NativeComInterfaces.IAdsPathname)new NativeComInterfaces.Pathname();
            return pathCracker.GetEscapedElement(0, originalPath);
        }

        internal static int Compare(string? s1, string? s2, uint compareFlags)
        {
            // This code block was specifically written for handling string comparison
            // involving null strings. The unmanaged API "Interop.Kernel32.CompareString"
            // does not handle null strings elegantly.
            //
            // This method handles comparison of the specified strings
            // if and only if either one of the two strings or both are null.
            if (s1 == null || s2 == null)
            {
                return string.Compare(s1, s2);
            }

            int result = 0;
            int cchCount1 = 0;
            int cchCount2 = 0;

            cchCount1 = s1.Length;
            cchCount2 = s2.Length;

            result = Interop.Kernel32.CompareString(LCID, compareFlags, s1, cchCount1, s2, cchCount2);

            if (result == 0)
            {
                throw ExceptionHelper.GetExceptionFromErrorCode(Marshal.GetLastPInvokeError());
            }

            return (result - 2); // to give the semantics of <0, ==0, >0
        }

        internal static int Compare(string? s1, string? s2)
        {
            return Compare(s1, s2, DEFAULT_CMP_FLAGS);
        }

        internal static int Compare(string s1, int offset1, int length1, string s2, int offset2, int length2)
        {
            ArgumentNullException.ThrowIfNull(s1);
            ArgumentNullException.ThrowIfNull(s2);
            return Compare(s1.Substring(offset1, length1), s2.Substring(offset2, length2));
        }

        internal static int Compare(string s1, int offset1, int length1, string s2, int offset2, int length2, uint compareFlags)
        {
            ArgumentNullException.ThrowIfNull(s1);
            ArgumentNullException.ThrowIfNull(s2);
            return Compare(s1.Substring(offset1, length1), s2.Substring(offset2, length2), compareFlags);
        }

        //  Split given server name string to server name and port number.
        //  e.g. serverName input   serverName return   portNumber
        //       DC1                DC1                 null
        //       IPv4:Port          IPv4                Port
        //       [IPv6]:Port        IPv6                Port
        internal static string SplitServerNameAndPortNumber(string serverName, out string? portNumber)
        {
            portNumber = null;

            int lastColon = serverName.LastIndexOf(':');
            if (lastColon == -1)
            {
                //no port number e.g. DC1, IPv4
                return serverName;
            }

            //extract IPv6 port number if any
            bool isBrace = serverName.StartsWith("[", StringComparison.Ordinal);
            if (isBrace)
            {
                if (serverName.EndsWith("]", StringComparison.Ordinal))
                {
                    //[IPv6]
                    serverName = serverName.Substring(1, serverName.Length - 2); //2 for []
                    return serverName;
                }
                int closingBrace = serverName.LastIndexOf("]:");
                if ((closingBrace == -1) || (closingBrace + 1 != lastColon))
                {
                    //error, return input string
                    return serverName;
                }
                //[IPv6]:Port
                portNumber = serverName.Substring(lastColon + 1);
                serverName = serverName.Substring(1, closingBrace - 1);
                return serverName;
            }

            //check if IPv6 address
            try
            {
                IPAddress address = IPAddress.Parse(serverName);
                if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
                {
                    //IPv6
                    return serverName;
                }
            }
            catch (FormatException)
            {
                //not the address
            }

            //extract port number e.g. DC1:Port, IPv4:Port
            portNumber = serverName.Substring(lastColon + 1);
            serverName = serverName.Substring(0, lastColon);
            return serverName;
        }

        private static string? s_NTAuthorityString;

        internal static string GetNtAuthorityString()
        {
            if (s_NTAuthorityString == null)
            {
                SecurityIdentifier sidLocalSystem = new SecurityIdentifier("S-1-5-18");
                NTAccount ntLocalSystem = (NTAccount)sidLocalSystem.Translate(typeof(NTAccount));
                int index = ntLocalSystem.Value.IndexOf('\\');
                Debug.Assert(index != -1);
                s_NTAuthorityString = ntLocalSystem.Value.Substring(0, index);
            }
            return s_NTAuthorityString;
        }

        internal static bool IsSamUser()
        {
            //
            // Basic algorithm
            //
            // Get SID of current user (via OpenThreadToken/GetTokenInformation/CloseHandle for TokenUser)
            //
            // Is the user SID of the form S-1-5-21-... (does GetSidIdentityAuthority(u) == 5 and GetSidSubauthority(u, 0) == 21)?
            // If NO ---> is local user
            // If YES --->
            //      Get machine domain SID (via LsaOpenPolicy/LsaQueryInformationPolicy for PolicyAccountDomainInformation/LsaClose)
            //      Does EqualDomainSid indicate the current user SID and the machine domain SID have the same domain?
            //      If YES -->
            //          IS the local machine a DC
            //          If NO --> is local user
            //         If YES --> is _not_ local user
            //      If NO --> is _not_ local user
            //

            IntPtr pCopyOfUserSid = IntPtr.Zero;
            IntPtr pMachineDomainSid = IntPtr.Zero;

            try
            {
                // Get the user's SID
                pCopyOfUserSid = GetCurrentUserSid();

                // Is it of S-1-5-21 form: Is the issuing authority NT_AUTHORITY and the RID NT_NOT_UNIQUE?
                SidType sidType = ClassifySID(pCopyOfUserSid);

                if (sidType == SidType.RealObject)
                {
                    // It's a domain SID.  Now, is the domain portion for the local machine, or something else?

                    // Get the machine domain SID
                    pMachineDomainSid = GetMachineDomainSid();

                    // Does the user SID have the same domain as the machine SID?
                    bool sameDomain = false;
                    bool success = global::Interop.Advapi32.EqualDomainSid(pCopyOfUserSid, pMachineDomainSid, ref sameDomain);

                    // Since both pCopyOfUserSid and pMachineDomainSid should always be account SIDs
                    Debug.Assert(success);

                    // If user SID is the same domain as the machine domain, and the machine is not a DC then the user is a local (machine) user
                    return sameDomain ? !IsMachineDC(null) : false;
                }
                else
                {
                    // It's not a domain SID, must be local (e.g., NT AUTHORITY\foo, or BUILTIN\foo)
                    return true;
                }
            }
            finally
            {
                if (pCopyOfUserSid != IntPtr.Zero)
                    Marshal.FreeHGlobal(pCopyOfUserSid);

                if (pMachineDomainSid != IntPtr.Zero)
                    Marshal.FreeHGlobal(pMachineDomainSid);
            }
        }


        internal static unsafe IntPtr GetCurrentUserSid()
        {
            SafeTokenHandle? tokenHandle = null;
            IntPtr pBuffer = IntPtr.Zero;

            try
            {
                //
                // Get the current user's SID
                //
                int error = 0;

                // Get the current thread's token
                if (!global::Interop.Advapi32.OpenThreadToken(
                                global::Interop.Kernel32.GetCurrentThread(),
                                TokenAccessLevels.Query, // TOKEN_QUERY
                                true,
                                out tokenHandle
                                ))
                {
                    if ((error = Marshal.GetLastPInvokeError()) == 1008) // ERROR_NO_TOKEN
                    {
                        Debug.Assert(tokenHandle.IsInvalid);
                        tokenHandle.Dispose();

                        // Current thread doesn't have a token, try the process
                        if (!global::Interop.Advapi32.OpenProcessToken(
                                        global::Interop.Kernel32.GetCurrentProcess(),
                                        (int)TokenAccessLevels.Query,
                                        out tokenHandle
                                        ))
                        {
                            int lastError = Marshal.GetLastPInvokeError();
                            throw new InvalidOperationException(SR.Format(SR.UnableToOpenToken, lastError));
                        }
                    }
                    else
                    {
                        throw new InvalidOperationException(SR.Format(SR.UnableToOpenToken, error));
                    }
                }

                Debug.Assert(!tokenHandle.IsInvalid);

                uint neededBufferSize = 0;

                // Retrieve the user info from the current thread's token
                // First, determine how big a buffer we need.
                bool success = global::Interop.Advapi32.GetTokenInformation(
                                        tokenHandle.DangerousGetHandle(),
                                        (uint)global::Interop.Advapi32.TOKEN_INFORMATION_CLASS.TokenUser,
                                        IntPtr.Zero,
                                        0,
                                        out neededBufferSize);

                int getTokenInfoError = 0;
                if ((getTokenInfoError = Marshal.GetLastPInvokeError()) != 122) // ERROR_INSUFFICIENT_BUFFER
                {
                    throw new InvalidOperationException(
                                    SR.Format(SR.UnableToRetrieveTokenInfo, getTokenInfoError));
                }

                // Allocate the necessary buffer.
                Debug.Assert(neededBufferSize > 0);
                pBuffer = Marshal.AllocHGlobal((int)neededBufferSize);

                // Load the user info into the buffer
                success = global::Interop.Advapi32.GetTokenInformation(
                                        tokenHandle.DangerousGetHandle(),
                                        (uint)global::Interop.Advapi32.TOKEN_INFORMATION_CLASS.TokenUser,
                                        pBuffer,
                                        neededBufferSize,
                                        out neededBufferSize);

                if (!success)
                {
                    int lastError = Marshal.GetLastPInvokeError();
                    throw new InvalidOperationException(
                                    SR.Format(SR.UnableToRetrieveTokenInfo, lastError));
                }

                // Retrieve the user's SID from the user info
                Interop.TOKEN_USER tokenUser = *(Interop.TOKEN_USER*)pBuffer;
                IntPtr pUserSid = tokenUser.sidAndAttributes.Sid;   // this is a reference into the NATIVE memory (into pBuffer)

                Debug.Assert(global::Interop.Advapi32.IsValidSid(pUserSid));

                // Now we make a copy of the SID to return
                int userSidLength = global::Interop.Advapi32.GetLengthSid(pUserSid);
                IntPtr pCopyOfUserSid = Marshal.AllocHGlobal(userSidLength);
                success = global::Interop.Advapi32.CopySid(userSidLength, pCopyOfUserSid, pUserSid);
                if (!success)
                {
                    int lastError = Marshal.GetLastPInvokeError();
                    throw new InvalidOperationException(
                                    SR.Format(SR.UnableToRetrieveTokenInfo, lastError));
                }

                return pCopyOfUserSid;
            }
            finally
            {
                tokenHandle?.Dispose();

                if (pBuffer != IntPtr.Zero)
                    Marshal.FreeHGlobal(pBuffer);
            }
        }

        internal static unsafe IntPtr GetMachineDomainSid()
        {
            SafeLsaPolicyHandle? policyHandle = null;
            IntPtr pBuffer = IntPtr.Zero;

            try
            {
                global::Interop.OBJECT_ATTRIBUTES oa = default;
                uint err = global::Interop.Advapi32.LsaOpenPolicy(
                                SystemName: null,
                                ref oa,
                                (int)global::Interop.Advapi32.PolicyRights.POLICY_VIEW_LOCAL_INFORMATION,
                                out policyHandle);

                if (err != 0)
                {
                    throw new InvalidOperationException(SR.Format(SR.UnableToRetrievePolicy, global::Interop.Advapi32.LsaNtStatusToWinError(err)));
                }

                Debug.Assert(!policyHandle.IsInvalid);
                err = global::Interop.Advapi32.LsaQueryInformationPolicy(
                                policyHandle.DangerousGetHandle(),
                                5,              // PolicyAccountDomainInformation
                                ref pBuffer);

                if (err != 0)
                {
                    throw new InvalidOperationException(SR.Format(SR.UnableToRetrievePolicy, global::Interop.Advapi32.LsaNtStatusToWinError(err)));
                }

                Debug.Assert(pBuffer != IntPtr.Zero);
                POLICY_ACCOUNT_DOMAIN_INFO info = *(POLICY_ACCOUNT_DOMAIN_INFO*)pBuffer;

                Debug.Assert(global::Interop.Advapi32.IsValidSid(info.DomainSid));

                // Now we make a copy of the SID to return
                int sidLength = global::Interop.Advapi32.GetLengthSid(info.DomainSid);
                IntPtr pCopyOfSid = Marshal.AllocHGlobal(sidLength);
                bool success = global::Interop.Advapi32.CopySid(sidLength, pCopyOfSid, info.DomainSid);
                if (!success)
                {
                    int lastError = Marshal.GetLastPInvokeError();
                    throw new InvalidOperationException(
                                    SR.Format(SR.UnableToRetrievePolicy, lastError));
                }

                return pCopyOfSid;
            }
            finally
            {
                policyHandle?.Dispose();

                if (pBuffer != IntPtr.Zero)
                    global::Interop.Advapi32.LsaFreeMemory(pBuffer);
            }
        }

        internal static bool IsMachineDC(string? computerName)
        {
            IntPtr dsRoleInfoPtr = IntPtr.Zero;
            int err = -1;

            try
            {
                if (null == computerName)
                    err = Interop.Netapi32.DsRoleGetPrimaryDomainInformation(null, Interop.Netapi32.DSROLE_PRIMARY_DOMAIN_INFO_LEVEL.DsRolePrimaryDomainInfoBasic, out dsRoleInfoPtr);
                else
                    err = Interop.Netapi32.DsRoleGetPrimaryDomainInformation(computerName, Interop.Netapi32.DSROLE_PRIMARY_DOMAIN_INFO_LEVEL.DsRolePrimaryDomainInfoBasic, out dsRoleInfoPtr);

                if (err != 0)
                {
                    throw new InvalidOperationException(
                                    SR.Format(
                                            SR.UnableToRetrieveDomainInfo,
                                            err));
                }

                DSROLE_PRIMARY_DOMAIN_INFO_BASIC dsRolePrimaryDomainInfo =
                    Marshal.PtrToStructure<DSROLE_PRIMARY_DOMAIN_INFO_BASIC>(dsRoleInfoPtr)!;

                return (dsRolePrimaryDomainInfo.MachineRole == DSROLE_MACHINE_ROLE.DsRole_RoleBackupDomainController ||
                             dsRolePrimaryDomainInfo.MachineRole == DSROLE_MACHINE_ROLE.DsRole_RolePrimaryDomainController);
            }
            finally
            {
                if (dsRoleInfoPtr != IntPtr.Zero)
                    Interop.Netapi32.DsRoleFreeMemory(dsRoleInfoPtr);
            }
        }

        internal static unsafe SidType ClassifySID(IntPtr pSid)
        {
            Debug.Assert(global::Interop.Advapi32.IsValidSid(pSid));

            // Get the issuing authority and the first RID
            IntPtr pIdentAuth = global::Interop.Advapi32.GetSidIdentifierAuthority(pSid);

            Interop.Advapi32.SID_IDENTIFIER_AUTHORITY identAuth = *(Interop.Advapi32.SID_IDENTIFIER_AUTHORITY*)pIdentAuth;

            IntPtr pRid = global::Interop.Advapi32.GetSidSubAuthority(pSid, 0);
            int rid = Marshal.ReadInt32(pRid);

            // These bit signify that the sid was issued by ADAM.  If so then it can't be a fake sid.
            if ((identAuth.b3 & 0xF0) == 0x10)
                return SidType.RealObject;

            // Is it S-1-5-...?
            if (!(identAuth.b1 == 0) &&
                  (identAuth.b2 == 0) &&
                  (identAuth.b3 == 0) &&
                  (identAuth.b4 == 0) &&
                  (identAuth.b5 == 0) &&
                  (identAuth.b6 == 5))
            {
                // No, so it can't be an account or builtin SID.
                // Probably something like \Everyone or \LOCAL.
                return SidType.FakeObject;
            }

            // Is the SID S-1-5-0-0-0-RID(sentinel SID)?
            if (IsSentinelSID(pSid))
            {
                return SidType.FakeObject;
            }

            return rid switch
            {
                21 => SidType.RealObject, // Account SID
                32 => SidType.RealObjectFakeDomain, // BUILTIN SID
                _ => SidType.FakeObject,
            };
        }


        internal static int GetLastRidFromSid(IntPtr pSid)
        {
            IntPtr pRidCount = global::Interop.Advapi32.GetSidSubAuthorityCount(pSid);
            int ridCount = Marshal.ReadByte(pRidCount);
            IntPtr pLastRid = global::Interop.Advapi32.GetSidSubAuthority(pSid, ridCount - 1);
            int lastRid = Marshal.ReadInt32(pLastRid);

            return lastRid;
        }

        internal static int GetLastRidFromSid(byte[] sid)
        {
            IntPtr pSid = IntPtr.Zero;

            try
            {
                pSid = Utils.ConvertByteArrayToIntPtr(sid);
                int rid = GetLastRidFromSid(pSid);

                return rid;
            }
            finally
            {
                if (pSid != IntPtr.Zero)
                    Marshal.FreeHGlobal(pSid);
            }
        }

        // The caller must call Marshal.FreeHGlobal on the returned
        // value to free it.
        internal static IntPtr ConvertByteArrayToIntPtr(byte[] bytes)
        {
            IntPtr pBytes = IntPtr.Zero;

            pBytes = Marshal.AllocHGlobal(bytes.Length);

            try
            {
                Marshal.Copy(bytes, 0, pBytes, bytes.Length);
            }
            catch (Exception)
            {
                Marshal.FreeHGlobal(pBytes);
                throw;
            }

            Debug.Assert(pBytes != IntPtr.Zero);
            return pBytes;
        }

        //
        // The sentinel SID were placed in the domain SID range S-1-5-21-X-Y-Z-R with R < 512 because the existing domain controllers would always filter those SIDs out at boundaries.
        // That way, the sentinel SID which says the claims or compound data is safe to consume would be removed should the claims or compound PAC ever pass through a domain controller
        // that did not know how to apply security checks. S-1-5-21-X-Y-Z-R means that the SID belongs to a domain(including the local account domain) unless X=Y=Z=0 in which
        // case it's a sentinel SID, a special type of pseudo-object that can't be interpreted in isolation.
        //
        internal static bool IsSentinelSID(IntPtr pSid)
        {
            Debug.Assert(global::Interop.Advapi32.IsValidSid(pSid));

            IntPtr psubAuthorityCount = global::Interop.Advapi32.GetSidSubAuthorityCount(pSid);
            int subAuthorityCount = Marshal.ReadByte(psubAuthorityCount);

            //
            // Sentinel SIDs are of format S-1-5-21-X-Y-Z-R, so if the subauthority count is not equal to 5
            // (21-X-Y-Z-R), then it is not a sentinel SID.
            //
            if (subAuthorityCount != 5)
            {
                return false;
            }

            //
            // If the rid is greater than equal to 512 then it is not a sentinel sid
            //
            int rid = GetLastRidFromSid(pSid);
            if (rid >= 512)
            {
                return false;
            }

            // We  are going to check for X, Y and Z only hence starting the for loop
            // with i = 1, and not reading sunAuthority-1 which is the RID
            for (int i = 1; i < subAuthorityCount - 1; i++)
            {
                IntPtr pcurrentSubauthority = global::Interop.Advapi32.GetSidSubAuthority(pSid, i);
                int currentSubauthority = Marshal.ReadInt32(pcurrentSubauthority);

                //
                // We return false as soon as we know the first subauthority is not 0
                //
                if (currentSubauthority != 0)
                {
                    return false;
                }
            }

            //
            // This means X=Y=Z=0
            //
            return true;
        }
    }
}
