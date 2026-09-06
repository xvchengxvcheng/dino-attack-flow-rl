using System;
using System.Collections.Generic;

namespace LlamAcademy.Dinos.Map
{
    public enum DinoTargetProfileId
    {
        Velociraptor,
        Pachycephalosaurus,
        TRex
    }

    public static class DinoTargetPriorityProfile
    {
        private static readonly IReadOnlyList<DinoTargetCategory> VelociraptorCategories =
            Array.AsReadOnly(new[]
            {
                DinoTargetCategory.House,
                DinoTargetCategory.Defender,
                DinoTargetCategory.Wall
            });

        private static readonly IReadOnlyList<DinoTargetCategory> PachycephalosaurusCategories =
            Array.AsReadOnly(new[]
            {
                DinoTargetCategory.Defender,
                DinoTargetCategory.House,
                DinoTargetCategory.Wall
            });

        private static readonly IReadOnlyList<DinoTargetCategory> TRexCategories =
            Array.AsReadOnly(new[]
            {
                DinoTargetCategory.Wall,
                DinoTargetCategory.Defender,
                DinoTargetCategory.House
            });

        public static IReadOnlyList<DinoTargetCategory> GetCategories(DinoTargetProfileId profileId)
        {
            return profileId switch
            {
                DinoTargetProfileId.Velociraptor => VelociraptorCategories,
                DinoTargetProfileId.Pachycephalosaurus => PachycephalosaurusCategories,
                DinoTargetProfileId.TRex => TRexCategories,
                _ => throw new ArgumentOutOfRangeException(nameof(profileId), profileId, null)
            };
        }
    }
}
