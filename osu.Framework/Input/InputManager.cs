﻿// Copyright (c) 2007-2017 ppy Pty Ltd <contact@ppy.sh>.
// Licensed under the MIT Licence - https://raw.githubusercontent.com/ppy/osu-framework/master/LICENCE

using OpenTK;
using OpenTK.Input;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Input.Handlers;
using osu.Framework.Platform;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace osu.Framework.Input
{
    public abstract class InputManager : Container, IRequireHighFrequencyMousePosition
    {
        /// <summary>
        /// The initial delay before key repeat begins.
        /// </summary>
        private const int repeat_initial_delay = 250;

        /// <summary>
        /// The delay between key repeats after the initial repeat.
        /// </summary>
        private const int repeat_tick_rate = 70;

        /// <summary>
        /// The maximum time between two clicks for a double-click to be considered.
        /// </summary>
        private const int double_click_time = 250;

        /// <summary>
        /// The distance that must be moved before a drag begins.
        /// </summary>
        private const float drag_start_distance = 0;

        /// <summary>
        /// The distance that must be moved until a dragged click becomes invalid.
        /// </summary>
        private const float click_drag_distance = 40;

        /// <summary>
        /// The time of the last input action.
        /// </summary>
        public double LastActionTime;

        protected GameHost Host;

        internal Drawable FocusedDrawable;

        protected abstract IEnumerable<InputHandler> InputHandlers { get; }

        private double lastClickTime;

        private double keyboardRepeatTime;

        private bool isDragging;

        private bool isValidClick;

        /// <summary>
        /// The last processed state.
        /// </summary>
        public InputState CurrentState = new InputState();

        /// <summary>
        /// The sequential list in which to handle mouse input.
        /// </summary>
        private readonly List<Drawable> mouseInputQueue = new List<Drawable>();

        /// <summary>
        /// The sequential list in which to handle keyboard input.
        /// </summary>
        private readonly List<Drawable> keyboardInputQueue = new List<Drawable>();

        private Drawable draggingDrawable;

        /// <summary>
        /// Contains the previously hovered <see cref="Drawable"/>s prior to when
        /// <see cref="hoveredDrawables"/> got updated.
        /// </summary>
        private readonly List<Drawable> lastHoveredDrawables = new List<Drawable>();

        /// <summary>
        /// Contains all hovered <see cref="Drawable"/>s in top-down order up to the first
        /// which returned true in its <see cref="Drawable.OnHover(InputState)"/> method.
        /// Top-down in this case means reverse draw order, i.e. the front-most visible
        /// <see cref="Drawable"/> first, and <see cref="Container"/>s after their children.
        /// </summary>
        private readonly List<Drawable> hoveredDrawables = new List<Drawable>();

        /// <summary>
        /// The <see cref="Drawable"/> which returned true in its
        /// <see cref="Drawable.OnHover(InputState)"/> method, or null if none did so.
        /// </summary>
        private Drawable hoverHandledDrawable;

        /// <summary>
        /// Contains all hovered <see cref="Drawable"/>s in top-down order up to the first
        /// which returned true in its <see cref="Drawable.OnHover(InputState)"/> method.
        /// Top-down in this case means reverse draw order, i.e. the front-most visible
        /// <see cref="Drawable"/> first, and <see cref="Container"/>s after their children.
        /// </summary>
        public IReadOnlyList<Drawable> HoveredDrawables => hoveredDrawables;

        /// <summary>
        /// Contains all <see cref="Drawable"/>s in top-down order which are considered
        /// for mouse input. This list is the same as <see cref="HoveredDrawables"/>, only
        /// that the return value of <see cref="Drawable.OnHover(InputState)"/> is not taken
        /// into account.
        /// </summary>
        public IReadOnlyList<Drawable> MouseInputQueue => mouseInputQueue;

        protected InputManager()
        {
            RelativeSizeAxes = Axes.Both;
        }

        [BackgroundDependencyLoader(permitNulls: true)]
        private void load(GameHost host)
        {
            Host = host;
        }

        /// <summary>
        /// Reset current focused drawable to the top-most drawable which is <see cref="Drawable.RequestsFocus"/>.
        /// </summary>
        public void TriggerFocusContention()
        {
            if (FocusedDrawable != this)
                ChangeFocus(null);
        }

        /// <summary>
        /// Changes the currently-focused drawable. First checks that <see cref="potentialFocusTarget"/> is in a valid state to receive focus,
        /// then unfocuses the current <see cref="FocusedDrawable"/> and focuses <see cref="potentialFocusTarget"/>.
        /// <see cref="potentialFocusTarget"/> can be null to reset focus.
        /// If the given drawable is already focused, nothing happens and no events are fired.
        /// </summary>
        /// <param name="potentialFocusTarget">The drawable to become focused.</param>
        /// <returns>True if the given drawable is now focused (or focus is dropped in the case of a null target).</returns>
        public bool ChangeFocus(Drawable potentialFocusTarget) => ChangeFocus(potentialFocusTarget, CurrentState);

        /// <summary>
        /// Changes the currently-focused drawable. First checks that <see cref="potentialFocusTarget"/> is in a valid state to receive focus,
        /// then unfocuses the current <see cref="FocusedDrawable"/> and focuses <see cref="potentialFocusTarget"/>.
        /// <see cref="potentialFocusTarget"/> can be null to reset focus.
        /// If the given drawable is already focused, nothing happens and no events are fired.
        /// </summary>
        /// <param name="potentialFocusTarget">The drawable to become focused.</param>
        /// <param name="state">The <see cref="InputState"/> associated with the focusing event.</param>
        /// <returns>True if the given drawable is now focused (or focus is dropped in the case of a null target).</returns>
        protected bool ChangeFocus(Drawable potentialFocusTarget, InputState state)
        {
            if (potentialFocusTarget == FocusedDrawable)
                return true;

            if (potentialFocusTarget != null && (!potentialFocusTarget.IsPresent || !potentialFocusTarget.AcceptsFocus))
                return false;

            var previousFocus = FocusedDrawable;

            FocusedDrawable = null;

            if (previousFocus != null)
            {
                previousFocus.HasFocus = false;
                previousFocus.TriggerOnFocusLost(state);

                if (FocusedDrawable != null) throw new InvalidOperationException($"Focus cannot be changed inside {nameof(OnFocusLost)}");
            }

            FocusedDrawable = potentialFocusTarget;

            if (FocusedDrawable != null)
            {
                FocusedDrawable.HasFocus = true;
                FocusedDrawable.TriggerOnFocus(state);
            }

            return true;
        }

        internal override bool BuildKeyboardInputQueue(List<Drawable> queue) => false;

        internal override bool BuildMouseInputQueue(Vector2 screenSpaceMousePos, List<Drawable> queue) => false;

        protected override void Update()
        {
            var pendingStates = createDistinctInputStates(GetPendingStates()).ToArray();

            unfocusIfNoLongerValid();

            //we need to make sure the code in the foreach below is run at least once even if we have no new pending states.
            if (pendingStates.Length == 0)
                pendingStates = new[] { new InputState() };

            foreach (InputState s in pendingStates)
            {
                bool hasNewKeyboard = s.Keyboard != null;
                bool hasNewMouse = s.Mouse != null;

                var last = CurrentState;

                //avoid lingering references that would stay forever.
                last.Last = null;

                CurrentState = s;
                CurrentState.Last = last;

                if (CurrentState.Keyboard == null) CurrentState.Keyboard = last.Keyboard ?? new KeyboardState();
                if (CurrentState.Mouse == null) CurrentState.Mouse = last.Mouse ?? new MouseState();

                TransformState(CurrentState);

                //move above?
                updateInputQueues(CurrentState);

                // we only want to set a last state if both the new and old state are of the same type.
                // this avoids giving the new state a false impression of being able to calculate delta values based on a last
                // state potentially from a different input source.
                if (last.Mouse != null && s.Mouse != null)
                {
                    if (last.Mouse.GetType() == s.Mouse.GetType())
                    {
                        last.Mouse.LastState = null;
                        s.Mouse.LastState = last.Mouse;
                    }

                    if (last.Mouse.HasAnyButtonPressed)
                        s.Mouse.PositionMouseDown = last.Mouse.PositionMouseDown;
                }

                //hover could change even when the mouse state has not.
                updateHoverEvents(CurrentState);

                if (hasNewMouse)
                    updateMouseEvents(CurrentState);

                if (hasNewKeyboard || CurrentState.Keyboard.Keys.Any())
                    updateKeyboardEvents(CurrentState);
            }

            if (CurrentState.Mouse != null)
            {
                foreach (var d in mouseInputQueue)
                    if (d is IRequireHighFrequencyMousePosition)
                        if (d.TriggerOnMouseMove(CurrentState)) break;
            }

            keyboardRepeatTime -= Time.Elapsed;

            if (FocusedDrawable == null)
                focusTopMostRequestingDrawable();

            base.Update();
        }

        /// <summary>
        /// In order to provide a reliable event system to drawables, we want to ensure that we reprocess input queues (via the
        /// main loop in<see cref="updateInputQueues(InputState)"/> after each and every button or key change. This allows
        /// correct behaviour in a case where the input queues change based on triggered by a button or key.
        /// </summary>
        /// <param name="states">A list of <see cref="InputState"/>s</param>
        /// <returns>Processed states such that at most one button change occurs between any two consecutive states.</returns>
        private IEnumerable<InputState> createDistinctInputStates(List<InputState> states)
        {
            InputState last = CurrentState;

            foreach (var i in states)
            {
                //first we want to create a copy of ourselves without any button changes
                //we do this by updating our buttons to the state of the last frame.
                var iWithoutButtons = i.Clone();

                var iHasMouse = iWithoutButtons.Mouse != null;
                var iHasKeyboard = iWithoutButtons.Keyboard != null;

                if (iHasMouse)
                    for (MouseButton b = 0; b < MouseButton.LastButton; b++)
                        iWithoutButtons.Mouse.SetPressed(b, last.Mouse?.IsPressed(b) ?? false);

                if (iHasKeyboard)
                    iWithoutButtons.Keyboard.Keys = last.Keyboard?.Keys ?? new Key[] { };

                //we start by adding this state to the processed list...
                yield return iWithoutButtons;
                last = iWithoutButtons;

                //and then iterate over each button/key change, adding intermediate states along the way.
                if (iHasMouse)
                {
                    for (MouseButton b = 0; b < MouseButton.LastButton; b++)
                    {
                        if (i.Mouse.IsPressed(b) != (last.Mouse?.IsPressed(b) ?? false))
                        {
                            var intermediateState = last.Clone();
                            if (intermediateState.Mouse == null) intermediateState.Mouse = new MouseState();

                            //add our single local change
                            intermediateState.Mouse.SetPressed(b, i.Mouse.IsPressed(b));

                            last = intermediateState;
                            yield return intermediateState;
                        }
                    }
                }

                if (iHasKeyboard)
                {
                    foreach (var releasedKey in last.Keyboard?.Keys.Except(i.Keyboard.Keys) ?? new Key[] { })
                    {
                        var intermediateState = last.Clone();
                        if (intermediateState.Keyboard == null) intermediateState.Keyboard = new KeyboardState();

                        intermediateState.Keyboard.Keys = intermediateState.Keyboard.Keys.Where(d => d != releasedKey);

                        last = intermediateState;
                        yield return intermediateState;
                    }

                    foreach (var pressedKey in i.Keyboard.Keys.Except(last.Keyboard?.Keys ?? new Key[] { }))
                    {
                        var intermediateState = last.Clone();
                        if (intermediateState.Keyboard == null) intermediateState.Keyboard = new KeyboardState();

                        intermediateState.Keyboard.Keys = intermediateState.Keyboard.Keys.Union(new[] { pressedKey });

                        last = intermediateState;
                        yield return intermediateState;
                    }
                }
            }
        }

        protected virtual List<InputState> GetPendingStates()
        {
            var pendingStates = new List<InputState>();

            foreach (var h in InputHandlers)
            {
                if (h.IsActive && h.Enabled)
                    pendingStates.AddRange(h.GetPendingStates());
                else
                    h.GetPendingStates();
            }

            return pendingStates;
        }

        protected virtual void TransformState(InputState inputState)
        {
        }

        private void updateInputQueues(InputState state)
        {
            keyboardInputQueue.Clear();
            mouseInputQueue.Clear();

            if (state.Keyboard != null)
                foreach (Drawable d in AliveInternalChildren)
                    d.BuildKeyboardInputQueue(keyboardInputQueue);

            if (state.Mouse != null)
                foreach (Drawable d in AliveInternalChildren)
                    d.BuildMouseInputQueue(state.Mouse.Position, mouseInputQueue);

            // Keyboard and mouse queues were created in back-to-front order.
            // We want input to first reach front-most drawables, so the queues
            // need to be reversed.
            keyboardInputQueue.Reverse();
            mouseInputQueue.Reverse();
        }

        private void updateHoverEvents(InputState state)
        {
            Drawable lastHoverHandledDrawable = hoverHandledDrawable;
            hoverHandledDrawable = null;

            lastHoveredDrawables.Clear();
            lastHoveredDrawables.AddRange(hoveredDrawables);
            hoveredDrawables.Clear();

            // First, we need to construct hoveredDrawables for the current frame
            foreach (Drawable d in mouseInputQueue)
            {
                hoveredDrawables.Add(d);

                // Don't need to re-hover those that are already hovered
                if (d.IsHovered)
                {
                    // Check if this drawable previously handled hover, and assume it would once more
                    if (d == lastHoverHandledDrawable)
                    {
                        hoverHandledDrawable = lastHoverHandledDrawable;
                        break;
                    }

                    continue;
                }

                d.IsHovered = true;
                if (d.TriggerOnHover(state))
                {
                    hoverHandledDrawable = d;
                    break;
                }
            }

            // Unhover all previously hovered drawables which are no longer hovered.
            foreach (Drawable d in lastHoveredDrawables.Except(hoveredDrawables))
            {
                d.IsHovered = false;
                d.TriggerOnHoverLost(state);
            }
        }

        private void updateKeyboardEvents(InputState state)
        {
            KeyboardState keyboard = (KeyboardState)state.Keyboard;

            if (!keyboard.Keys.Any())
                keyboardRepeatTime = 0;

            var last = state.Last?.Keyboard;

            if (last == null) return;

            foreach (var k in last.Keys)
            {
                if (!keyboard.Keys.Contains(k))
                    handleKeyUp(state, k);
            }

            foreach (Key k in keyboard.Keys.Distinct())
            {
                bool isModifier = k == Key.LControl || k == Key.RControl
                                  || k == Key.LAlt || k == Key.RAlt
                                  || k == Key.LShift || k == Key.RShift
                                  || k == Key.LWin || k == Key.RWin;

                LastActionTime = Time.Current;

                bool isRepetition = last.Keys.Contains(k);

                if (isModifier)
                {
                    //modifiers shouldn't affect or report key repeat
                    if (!isRepetition)
                        handleKeyDown(state, k, false);
                    continue;
                }

                if (isRepetition)
                {
                    if (keyboardRepeatTime <= 0)
                    {
                        keyboardRepeatTime += repeat_tick_rate;
                        handleKeyDown(state, k, true);
                    }
                }
                else
                {
                    keyboardRepeatTime = repeat_initial_delay;
                    handleKeyDown(state, k, false);
                }
            }
        }

        private List<Drawable> mouseDownInputQueue;

        private void updateMouseEvents(InputState state)
        {
            MouseState mouse = (MouseState)state.Mouse;

            var last = state.Last?.Mouse as MouseState;

            if (last == null) return;

            if (mouse.Position != last.Position)
            {
                handleMouseMove(state);
                if (isDragging)
                    handleMouseDrag(state);
            }

            for (MouseButton b = 0; b < MouseButton.LastButton; b++)
            {
                var lastPressed = last.IsPressed(b);

                if (lastPressed != mouse.IsPressed(b))
                {
                    if (lastPressed)
                        handleMouseUp(state, b);
                    else
                        handleMouseDown(state, b);
                }
            }

            if (mouse.WheelDelta != 0)
                handleWheel(state);

            if (mouse.HasAnyButtonPressed)
            {
                if (!last.HasAnyButtonPressed)
                {
                    //stuff which only happens once after the mousedown state
                    mouse.PositionMouseDown = state.Mouse.Position;
                    LastActionTime = Time.Current;

                    if (mouse.IsPressed(MouseButton.Left))
                    {
                        isValidClick = true;

                        if (Time.Current - lastClickTime < double_click_time)
                        {
                            if (handleMouseDoubleClick(state))
                                //when we handle a double-click we want to block a normal click from firing.
                                isValidClick = false;

                            lastClickTime = 0;
                        }

                        lastClickTime = Time.Current;
                    }
                }

                if (!isDragging && Vector2.Distance(mouse.PositionMouseDown ?? mouse.Position, mouse.Position) > drag_start_distance)
                {
                    isDragging = true;
                    handleMouseDragStart(state);
                }
            }
            else if (last.HasAnyButtonPressed)
            {
                if (isValidClick && (draggingDrawable == null || Vector2.Distance(mouse.PositionMouseDown ?? mouse.Position, mouse.Position) < click_drag_distance))
                    handleMouseClick(state);

                mouseDownInputQueue = null;
                mouse.PositionMouseDown = null;
                isValidClick = false;

                if (isDragging)
                {
                    isDragging = false;
                    handleMouseDragEnd(state);
                }
            }
        }

        private bool handleMouseDown(InputState state, MouseButton button)
        {
            MouseDownEventArgs args = new MouseDownEventArgs
            {
                Button = button
            };

            mouseDownInputQueue = new List<Drawable>(mouseInputQueue);

            return mouseInputQueue.Find(target => target.TriggerOnMouseDown(state, args)) != null;
        }

        private bool handleMouseUp(InputState state, MouseButton button)
        {
            if (mouseDownInputQueue == null) return false;

            MouseUpEventArgs args = new MouseUpEventArgs
            {
                Button = button
            };

            //extra check for IsAlive because we are using an outdated queue.
            return mouseDownInputQueue.Any(target => target.IsAlive && target.IsPresent && target.TriggerOnMouseUp(state, args));
        }

        private bool handleMouseMove(InputState state)
        {
            return mouseInputQueue.Any(target => target.TriggerOnMouseMove(state));
        }

        private bool handleMouseClick(InputState state)
        {
            var intersectingQueue = mouseInputQueue.Intersect(mouseDownInputQueue);

            Drawable focusTarget = null;

            // click pass, triggering an OnClick on all drawables up to the first which returns true.
            // an extra IsHovered check is performed because we are using an outdated queue (for valid reasons which we need to document).
            var clickedDrawable = intersectingQueue.FirstOrDefault(t => t.CanReceiveInput && t.ReceiveMouseInputAt(state.Mouse.Position) && t.TriggerOnClick(state));

            if (clickedDrawable != null)
            {
                focusTarget = clickedDrawable;

                if (!focusTarget.AcceptsFocus)
                {
                    // search upwards from the clicked drawable until we find something to handle focus.
                    Drawable previousFocused = FocusedDrawable;

                    while (focusTarget?.AcceptsFocus == false)
                        focusTarget = focusTarget.Parent;

                    if (focusTarget != null && previousFocused != null)
                    {
                        // we found a focusable target above us.
                        // now search upwards from previousFocused to check whether focusTarget is a common parent.
                        Drawable search = previousFocused;
                        while (search != null && search != focusTarget)
                            search = search.Parent;

                        if (focusTarget == search)
                            // we have a common parent, so let's keep focus on the previously focused target.
                            focusTarget = previousFocused;
                    }
                }
            }

            ChangeFocus(focusTarget, state);
            return clickedDrawable != null;
        }

        private bool handleMouseDoubleClick(InputState state)
        {
            return mouseInputQueue.Any(target => target.TriggerOnDoubleClick(state));
        }

        private bool handleMouseDrag(InputState state)
        {
            //Once a drawable is dragged, it remains in a dragged state until the drag is finished.
            return draggingDrawable?.TriggerOnDrag(state) ?? false;
        }

        private bool handleMouseDragStart(InputState state)
        {
            Trace.Assert(draggingDrawable == null, "The draggingDrawable was not set to null by handleMouseDragEnd.");
            draggingDrawable = mouseDownInputQueue?.FirstOrDefault(target => target.IsAlive && target.TriggerOnDragStart(state));
            if (draggingDrawable != null)
                draggingDrawable.IsDragged = true;
            return draggingDrawable != null;
        }

        private bool handleMouseDragEnd(InputState state)
        {
            if (draggingDrawable == null)
                return false;

            bool result = draggingDrawable.TriggerOnDragEnd(state);
            draggingDrawable.IsDragged = false;
            draggingDrawable = null;

            return result;
        }

        private bool handleWheel(InputState state)
        {
            return mouseInputQueue.Any(target => target.TriggerOnWheel(state));
        }

        private bool handleKeyDown(InputState state, Key key, bool repeat)
        {
            KeyDownEventArgs args = new KeyDownEventArgs
            {
                Key = key,
                Repeat = repeat
            };

            if (!unfocusIfNoLongerValid())
            {
                if (args.Key == Key.Escape)
                {
                    ChangeFocus(null);
                    return true;
                }
                if (FocusedDrawable.TriggerOnKeyDown(state, args))
                    return true;
            }

            return keyboardInputQueue.Any(target => target.TriggerOnKeyDown(state, args));
        }

        private bool handleKeyUp(InputState state, Key key)
        {
            KeyUpEventArgs args = new KeyUpEventArgs
            {
                Key = key
            };

            if (!unfocusIfNoLongerValid() && (FocusedDrawable?.TriggerOnKeyUp(state, args) ?? false))
                return true;

            return keyboardInputQueue.Any(target => target.TriggerOnKeyUp(state, args));
        }

        /// <summary>
        /// Unfocus the current focused drawable if it is no longer in a valid state.
        /// </summary>
        /// <returns>true if there is no longer a focus.</returns>
        private bool unfocusIfNoLongerValid()
        {
            if (FocusedDrawable == null) return true;

            bool stillValid = FocusedDrawable.IsPresent && FocusedDrawable.Parent != null;

            if (stillValid)
            {
                //ensure we are visible
                CompositeDrawable d = FocusedDrawable.Parent;
                while (d != null)
                {
                    if (!d.IsPresent)
                    {
                        stillValid = false;
                        break;
                    }
                    d = d.Parent;
                }
            }

            if (stillValid)
                return false;

            ChangeFocus(null);
            return true;
        }

        private void focusTopMostRequestingDrawable() => ChangeFocus(keyboardInputQueue.FirstOrDefault(target => target.RequestsFocus));
    }

    public enum ConfineMouseMode
    {
        Never,
        Fullscreen,
        Always
    }
}
