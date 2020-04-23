﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis.Rename.ConflictEngine;
using Microsoft.CodeAnalysis.Shared.Extensions;
using Microsoft.CodeAnalysis.Text;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.Rename
{
    internal readonly struct ConflictResolution
    {
        public readonly Solution NewSolution;
        public readonly Solution OldSolution;

        public readonly ImmutableArray<RelatedLocation> RelatedLocations;
        public readonly bool ReplacementTextValid;

        private readonly ImmutableDictionary<DocumentId, ImmutableArray<(TextSpan oldSpan, TextSpan newSpan)>> _documentToModifiedSpansMap;
        private readonly ImmutableDictionary<DocumentId, ImmutableArray<ComplexifiedSpan>> _documentToComplexifiedSpansMap;

        private readonly ImmutableDictionary<DocumentId, ImmutableArray<RelatedLocation>> _documentToRelatedLocationsMap;

        /// <summary>
        /// The list of all document ids of documents that have been touched for this rename operation.
        /// </summary>
        public readonly ImmutableArray<DocumentId> DocumentIds;

        public ConflictResolution(MutableConflictResolution resolution)
        {
            OldSolution = resolution.OldSolution;
            NewSolution = resolution.NewSolution;
            RelatedLocations = resolution.RelatedLocations.ToImmutableArray();
            ReplacementTextValid = resolution.ReplacementTextValid;

            DocumentIds = resolution.RenamedSpansTracker.DocumentIds.Concat(
                resolution.RelatedLocations.Select(l => l.DocumentId)).Distinct().ToImmutableArray();

            _documentToModifiedSpansMap = resolution.RenamedSpansTracker.GetDocumentToModifiedSpansMap();
            _documentToComplexifiedSpansMap = resolution.RenamedSpansTracker.GetDocumentToComplexifiedSpansMap();
            _documentToRelatedLocationsMap = resolution.RelatedLocations.GroupBy(loc => loc.DocumentId).ToImmutableDictionary(
                g => g.Key, g => g.ToImmutableArray());
        }

        public ImmutableArray<(TextSpan oldSpan, TextSpan newSpan)> GetComplexifiedSpans(DocumentId documentId)
            => _documentToComplexifiedSpansMap.TryGetValue(documentId, out var complexifiedSpans)
                ? complexifiedSpans.SelectAsArray(c => (c.OriginalSpan, c.NewSpan))
                : ImmutableArray<(TextSpan oldSpan, TextSpan newSpan)>.Empty;

        public ImmutableDictionary<TextSpan, TextSpan> GetModifiedSpanMap(DocumentId documentId)
        {
            var result = ImmutableDictionary.CreateBuilder<TextSpan, TextSpan>();
            if (_documentToModifiedSpansMap.TryGetValue(documentId, out var modifiedSpans))
            {
                foreach (var (oldSpan, newSpan) in modifiedSpans)
                    result[oldSpan] = newSpan;
            }

            if (_documentToComplexifiedSpansMap.TryGetValue(documentId, out var complexifiedSpans))
            {
                foreach (var complexifiedSpan in complexifiedSpans)
                {
                    foreach (var (oldSpan, newSpan) in complexifiedSpan.ModifiedSubSpans)
                        result[oldSpan] = newSpan;
                }
            }

            return result.ToImmutable();
        }

        public ImmutableArray<RelatedLocation> GetRelatedLocationsForDocument(DocumentId documentId)
            => _documentToRelatedLocationsMap.TryGetValue(documentId, out var result)
                ? result
                : ImmutableArray<RelatedLocation>.Empty;

        internal TextSpan GetResolutionTextSpan(TextSpan originalSpan, DocumentId documentId)
        {
            if (_documentToModifiedSpansMap.TryGetValue(documentId, out var modifiedSpans))
            {
                var first = modifiedSpans.FirstOrNull(t => t.oldSpan == originalSpan);
                if (first.HasValue)
                    return first.Value.newSpan;
            }

            if (_documentToComplexifiedSpansMap.TryGetValue(documentId, out var complexifiedSpans))
            {
                var first = complexifiedSpans.FirstOrNull(c => c.OriginalSpan.Contains(originalSpan));
                if (first.HasValue)
                    return first.Value.NewSpan;
            }

            // The RenamedSpansTracker doesn't currently track unresolved conflicts for
            // unmodified locations.  If the document wasn't modified, we can just use the 
            // original span as the new span.
            return originalSpan;
        }
    }
}
