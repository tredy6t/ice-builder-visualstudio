﻿// **********************************************************************
//
// Copyright (c) 2009-2018 ZeroC, Inc. All rights reserved.
//
// **********************************************************************

using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MSProject = Microsoft.Build.Evaluation.Project;

namespace IceBuilder
{
    public static class IVsProjectExtension
    {
        private static void EnsureIsCheckout(IVsProject project)
        {
            EnsureIsCheckout(project.GetDTEProject(), project.GetProjectFullPath());
        }

        private static void EnsureIsCheckout(EnvDTE.Project project, string path)
        {
            var sc = project.DTE.SourceControl;
            if(sc != null)
            {
                if(sc.IsItemUnderSCC(path) && !sc.IsItemCheckedOut(path))
                {
                    sc.CheckOutItem(path);
                }
            }
        }

        public static EnvDTE.Project GetDTEProject(this IVsProject project)
        {
            IVsHierarchy hierarchy = project as IVsHierarchy;
            object obj = null;
            if (hierarchy != null)
            {
                hierarchy.GetProperty(VSConstants.VSITEMID_ROOT, (int)__VSHPROPID.VSHPROPID_ExtObject, out obj);
            }
            var dteproject = obj as EnvDTE.Project;
            EnsureIsCheckout(dteproject, project.GetProjectFullPath());
            return dteproject;
        }

        public static List<string> GetIceBuilderItems(this IVsProject project)
        {
            return project.WithProject((MSProject msproject) =>
                msproject.Items.Where(item => item.ItemType.Equals("SliceCompile"))
                               .Select(item => item.EvaluatedInclude)
                               .ToList());
        }

        public static MSProject GetMSBuildProject(this IVsProject project)
        {
            EnsureIsCheckout(project);
            return MSProjectExtension.LoadedProject(project.GetProjectFullPath());
        }

        public static string GetProjectBaseDirectory(this IVsProject project)
        {
            string fullPath;
            ErrorHandler.ThrowOnFailure(project.GetMkDocument(VSConstants.VSITEMID_ROOT, out fullPath));
            return Path.GetFullPath(Path.GetDirectoryName(fullPath));
        }

        public static string GetProjectFullPath(this IVsProject project)
        {
            try
            {
                string fullPath;
                ErrorHandler.ThrowOnFailure(project.GetMkDocument(VSConstants.VSITEMID_ROOT, out fullPath));
                return Path.GetFullPath(fullPath);
            }
            catch(NotImplementedException)
            {
                return string.Empty;
            }
        }

        //
        // Get the Guid that idenifies the type of the project
        //
        public static Guid GetProjecTypeGuid(this IVsProject project)
        {
            IVsHierarchy hierarchy = project as IVsHierarchy;
            if(hierarchy != null)
            {
                try
                {
                    Guid type;
                    ErrorHandler.ThrowOnFailure(hierarchy.GetGuidProperty(VSConstants.VSITEMID_ROOT, (int)__VSHPROPID.VSHPROPID_TypeGuid, out type));
                    return type;
                }
                catch(Exception)
                {
                }
            }
            return new Guid();
        }

        public static bool IsCppProject(this IVsProject project)
        {
            Guid type = project.GetProjecTypeGuid();
            return type.Equals(cppProjectGUID) || type.Equals(cppStoreAppProjectGUID);
        }

        public static bool IsCSharpProject(this IVsProject project)
        {
            Guid type = project.GetProjecTypeGuid();
            return type.Equals(csharpProjectGUID);
        }

        public static bool IsMSBuildIceBuilderInstalled(this IVsProject project)
        {
            var type = project.GetProjecTypeGuid();
            if(type.Equals(cppProjectGUID) || type.Equals(cppStoreAppProjectGUID) || type.Equals(csharpProjectGUID))
            {
                //
                // Find the full path of MSBuild Ice Builder props and target files and check they exists
                //
                var props = project.WithProject((MSProject msproject) =>
                    {
                        return msproject.Imports.Where(
                            import => import.ImportedProject.FullPath.EndsWith("zeroc.icebuilder.msbuild.props")).Select(
                            import => import.ImportedProject.FullPath).FirstOrDefault();
                    });

                var targets = project.WithProject((MSProject msproject) =>
                    {
                        return msproject.Imports.Where(
                            import => import.ImportedProject.FullPath.EndsWith("zeroc.icebuilder.msbuild.targets")).Select(
                            import => import.ImportedProject.FullPath).FirstOrDefault();
                    });
                return !string.IsNullOrEmpty(props) && !string.IsNullOrEmpty(targets) && File.Exists(props) && File.Exists(targets);
            }
            return false;
        }

        public static bool IsIceBuilderGeneratedItem(this IVsProject project, string path)
        {
            var projectDir = project.GetProjectBaseDirectory();
            var includeValue = FileUtil.RelativePath(projectDir, path);
            return project.WithProject((MSProject msproject) =>
                {
                    return msproject.AllEvaluatedItems.FirstOrDefault(
                        item =>
                        {
                            return (item.ItemType.Equals("Compile") ||
                                    item.ItemType.Equals("ClCompile") ||
                                    item.ItemType.Equals("ClInclude")) &&
                                    item.EvaluatedInclude.Equals(includeValue) &&
                                    item.HasMetadata("SliceCompileSource");
                        }) != null;
                });
        }

        public static void UpdateProject(this IVsProject project, Action<MSProject> action)
        {
            ProjectFactoryHelperInstance.ProjectHelper.UpdateProject(project, action);
        }

        public static T WithProject<T>(this IVsProject project, Func<MSProject, T> func)
        {
            return ProjectFactoryHelperInstance.ProjectHelper.WithProject(project, func);
        }

        public static IDisposable OnProjectUpdate(this IVsProject project, Action onProjectUpdate)
        {
            return ProjectFactoryHelperInstance.ProjectHelper.OnProjectUpdate(project, onProjectUpdate);
        }

        public static string GetItemMetadata(this IVsProject project, string identity, string name, string defaultValue = "")
        {
            return ProjectFactoryHelperInstance.ProjectHelper.GetItemMetadata(project, identity, name, defaultValue);
        }

        public static string GetDefaultItemMetadata(this IVsProject project, string name, bool evaluated, string defaultValue = "")
        {
            return ProjectFactoryHelperInstance.ProjectHelper.GetDefaultItemMetadata(project, name, evaluated, defaultValue);
        }

        public static void SetItemMetadata(this IVsProject project, string itemType, string label, string name, string value)
        {
            ProjectFactoryHelperInstance.ProjectHelper.SetItemMetadata(project, itemType, label, name, value);
        }

        public static void SetItemMetadata(this IVsProject project, string name, string value)
        {
            ProjectFactoryHelperInstance.ProjectHelper.SetItemMetadata(project, name, value);
        }

        public static void AddProjectFlavorIfNotExists(this IVsProject project, string flavor)
        {
            ProjectFactoryHelperInstance.ProjectHelper.UpdateProject(project,
                (MSProject msproject) =>
                {
                    msproject.AddProjectFlavorIfNotExists(flavor);
                });
        }

        public static string GetEvaluatedProperty(this IVsProject project, string name)
        {
            return project.GetEvaluatedProperty(name, string.Empty);
        }

        public static string GetProperty(this IVsProject project, string name)
        {
            return project.WithProject((MSProject msproject) =>
                {
                    return msproject.GetProperty(name, true);
                });
        }

        public static string GetPropertyWithDefault(this IVsProject project, string name, string defaultValue)
        {
            return project.WithProject((MSProject msproject) =>
            {
                return msproject.GetPropertyWithDefault(name, defaultValue);
            });
        }

        public static string GetEvaluatedProperty(this IVsProject project, string name, string defaultValue)
        {
            return project.WithProject((MSProject msproject) =>
            {
                var value = msproject.GetEvaluatedProperty(name);
                return string.IsNullOrEmpty(value) ? defaultValue : value;
            });
        }

        public static readonly Guid cppProjectGUID =
           new Guid("{8BC9CEB8-8B4A-11D0-8D11-00A0C91BC942}");
        public static readonly Guid cppStoreAppProjectGUID =
            new Guid("{BC8A1FFA-BEE3-4634-8014-F334798102B3}");
        public static readonly Guid csharpProjectGUID =
            new Guid("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}");
        public static readonly Guid unloadedProjectGUID =
            new Guid("{67294A52-A4F0-11D2-AA88-00C04F688DDE}");
    }
}
