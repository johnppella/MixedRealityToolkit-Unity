using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// enum describing range of affine xforms that are allowed.
/// </summary>
public enum TwoHandedManipulationType
{
    None = 0,
    Scale,
    Rotate,
    Move,
    MoveRotate,
    MoveScale,
    RotateScale,
    MoveRotateScale
};