﻿// Copyright (c) 2007-2017 ppy Pty Ltd <contact@ppy.sh>.
// Licensed under the MIT Licence - https://raw.githubusercontent.com/ppy/osu-framework/master/LICENCE

using OpenTK;
using osu.Framework.Graphics.Containers;
using osu.Framework.Lists;
using osu.Framework.Timing;

namespace osu.Framework.Graphics
{
    /// <summary>
    /// Exposes various properties that are part of the public interface of <see cref="Drawable"/>.
    /// This interface should generally NOT be implemented by other classes than <see cref="Drawable"/>, but only used to
    /// specify that an object is of type <see cref="Drawable"/>.
    /// It is mostly useful in cases where you need to specify additional constraints on a <see cref="Drawable"/>, but also do not want to force inheriting from
    /// any particular subclass of <see cref="Drawable"/>.
    /// </summary>
    public interface IDrawable : IHasLifetime
    {
        /// <summary>
        /// Absolute size of this Drawable in the <see cref="Parent"/>'s coordinate system.
        /// </summary>
        Vector2 DrawSize { get; }

        /// <summary>
        /// Contains a linear transformation, colour information, and blending information
        /// of this drawable.
        /// </summary>
        DrawInfo DrawInfo { get; }

        /// <summary>
        /// The parent of this drawable in the scene graph.
        /// </summary>
        CompositeDrawable Parent { get; }

        /// <summary>
        /// Whether this drawable is present for any sort of user-interaction.
        /// If this is false, then this drawable will not be drawn, it will not handle input,
        /// and it will not affect layouting (e.g. autosizing and flow).
        /// </summary>
        bool IsPresent { get; }

        /// <summary>
        /// The clock of this drawable. Used for keeping track of time across frames.
        /// </summary>
        IFrameBasedClock Clock { get; }

        /// <summary>
        /// Accepts a vector in local coordinates and converts it to coordinates in another Drawable's space.
        /// </summary>
        /// <param name="input">A vector in local coordinates.</param>
        /// <param name="other">The drawable in which space we want to transform the vector to.</param>
        /// <returns>The vector in other's coordinates.</returns>
        Vector2 ToSpaceOfOtherDrawable(Vector2 input, IDrawable other);

        /// <summary>
        /// Convert a position to the local coordinate system from either native or local to another drawable.
        /// This is *not* the same space as the Position member variable (use Parent.GetLocalPosition() in this case).
        /// </summary>
        /// <param name="screenSpacePos">The input position.</param>
        /// <returns>The output position.</returns>
        Vector2 ToLocalSpace(Vector2 screenSpacePos);

        /// <summary>
        /// Determines how this Drawable is blended with other already drawn Drawables.
        /// </summary>
        BlendingMode BlendingMode { get; }

        /// <summary>
        /// Whether this Drawable is currently hovered over.
        /// </summary>
        bool IsHovered { get; }

        /// <summary>
        /// Whether this Drawable is currently dragged.
        /// </summary>
        bool IsDragged { get; }

        /// <summary>
        /// Multiplicative alpha factor applied on top of <see cref="Colour.ColourInfo"/> and its existing
        /// alpha channel(s).
        /// </summary>
        float Alpha { get; }

        /// <summary>
        /// Show sprite instantly.
        /// </summary>
        void Show();

        /// <summary>
        /// Hide sprite instantly.
        /// </summary>
        void Hide();
    }
}
