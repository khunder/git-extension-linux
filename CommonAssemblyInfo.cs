// Copyright (C) 2026 Zied, GPL V3
// Copyright (C) GPL V3 https://github.com/gitextensions/gitextensions/blob/master/LICENSE.md
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.
using System.Reflection;
using System.Runtime.InteropServices;

// Only assemblies that target Windows (net*-windows) are Windows-only; the portable core is not.
#if WINDOWS7_0_OR_GREATER
[assembly: System.Runtime.Versioning.SupportedOSPlatform("windows7.0")]
#endif

// General Information about an assembly is controlled through the following
// set of attributes. Change these attribute values to modify the information
// associated with an assembly.
[assembly: AssemblyTitle("Git Extensions")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("Git Extensions")]
[assembly: AssemblyProduct("Git Extensions")]
[assembly: AssemblyCopyright("Copyright © 2008-2026 Git Extensions Team")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

// Version information for an assembly consists of the following four values:
//
//      Major Version
//      Minor Version
//      Build Number
//      Revision
//
[assembly: AssemblyVersion("33.33.33")]
[assembly: AssemblyFileVersion("33.33.33")]
[assembly: AssemblyInformationalVersion("33.33.33")]

// Disable CLS compliance. See https://github.com/gitextensions/gitextensions/issues/4710
[assembly: CLSCompliant(isCompliant: false)]

// Setting ComVisible to false makes the types in this assembly not visible
// to COM components.  If you need to access a type in this assembly from
// COM, set the ComVisible attribute to true on that type.
[assembly: ComVisible(false)]
