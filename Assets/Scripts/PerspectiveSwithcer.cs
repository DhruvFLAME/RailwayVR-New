// PerspectiveSwitcher.cs
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Splines;
using System.Linq;

public class PerspectiveSwitcher : MonoBehaviour
{
    [Header("References")]
    public Transform xrOrigin;
    public Transform cartSeatAnchor;
    public Transform freeViewAnchor;

    [Header("Linked Systems")]
    [Tooltip("Cart that should pause while the player is on the platform.")]
    public RollercoasterCart cart;

    [Tooltip("Drawer whose in-progress sketch must be cancelled on toggle.")]
    public VRTrackDrawer drawer;

    [Tooltip("SplineContainer to watch for changes — used to know when to reset the cart.")]
    public SplineContainer splineContainer;

    [Header("Input")]
    public InputActionProperty toggleAction;
    public KeyCode keyboardToggleKey = KeyCode.E;

    [Header("State")]
    [SerializeField] private bool inCart = true;

    private bool _splineDirty = false;   // set true while in free mode if drawer commits a track

    private void OnEnable()
    {
        var action = toggleAction.action;
        if (action != null)
        {
            action.performed += OnToggle;
            action.Enable();
        }

        Spline.Changed += OnSplineChanged;
    }

    private void OnDisable()
    {
        var action = toggleAction.action;
        if (action != null)
        {
            action.performed -= OnToggle;
            action.Disable();
        }

        Spline.Changed -= OnSplineChanged;
    }

    private void OnSplineChanged(Spline s, int knotIndex, SplineModification mod)
    {
        // Only flag if we're outside the cart — that's when "the track changed
        // while the cart wasn't using it" matters for the reset on re-entry.
        if (!inCart && splineContainer != null && splineContainer.Splines.Contains(s))
            _splineDirty = true;
    }

    private void Update()
    {
        if (Input.GetKeyDown(keyboardToggleKey))
            TogglePerspective();

        if (inCart && cartSeatAnchor != null && xrOrigin != null)
            xrOrigin.SetPositionAndRotation(cartSeatAnchor.position, cartSeatAnchor.rotation);
    }

    private void OnToggle(InputAction.CallbackContext _) => TogglePerspective();

    public void TogglePerspective()
    {
        // 1. Always abort any active draw — prevents teleport-induced rubber-banding.
        if (drawer != null && drawer.IsDrawing)
            drawer.CancelDrawing();

        inCart = !inCart;

        if (inCart)
        {
            // Returning to cart
            if (_splineDirty && cart != null)
            {
                cart.ResetToStart();   // recomputes length, snaps t=0
                _splineDirty = false;
            }
            if (cart != null) cart.StartCart();
        }
        else
        {
            // Going to platform
            if (cart != null) cart.StopCart();

            if (freeViewAnchor != null && xrOrigin != null)
                xrOrigin.SetPositionAndRotation(freeViewAnchor.position, freeViewAnchor.rotation);
        }

        Debug.Log($"[PerspectiveSwitcher] Now in {(inCart ? "CART" : "FREE")} mode.");
    }
}
