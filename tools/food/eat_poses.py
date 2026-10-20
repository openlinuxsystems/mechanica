"""Draft hold and eat poses for food, in build_characters.py's clip-spec language (to move there once approved)."""

from __future__ import annotations

import math

import numpy as np

import build_characters as bc

FOOD_GRIP = np.array([-0.12, -0.27, 1.12])          # right fist centre while holding a piece of food out in front
MOUTH = np.array([-0.01, -0.13, 1.56])


def food_arm(grip=FOOD_GRIP):
    # palm up, fingers forward: the fruit sits in the cupped hand
    return bc.arm_ik_from_grip("r", grip, d=(0.2, -0.95, 0.2), w=(-0.95, -0.2, 0.05), pole=(-0.7, 0.4, -1.0))


def mouth_arm(off=(0.0, -0.07, -0.032)):
    # the hand under the chin, palm up and toward the face: the fruit on it is at the lips
    return bc.arm_ik_from_grip("r", MOUTH + np.array(off), d=(0.35, -0.75, 0.55), w=(-0.93, -0.28, 0.21),
                               pole=(-0.8, 0.2, -1.0))


def clip_hold_food(t, T=2.0):
    ph = 2 * math.pi * t / T
    s = bc.upper_base(t, T)
    bc.relaxed_left(s, ph)
    s["arm_r"] = food_arm(FOOD_GRIP + np.array([0, 0, 0.004 * math.sin(ph)]))
    s["fingers_r"] = 38.0
    s["thumb_r"] = 22.0
    return s


def clip_eat_food(t):
    """1.4 s: up to the mouth, a bite with the head dipping into it, a second bite, then down, chewing."""
    h = clip_hold_food(0.0)
    up = bc.with_right_arm(h, arm_r=mouth_arm(), neck=(4.0, 0, 0), head=(6.0, 0, 0), chest=(2.0, 0, 0))
    bite = bc.with_right_arm(up, arm_r=mouth_arm((0.0, -0.058, -0.032)), neck=(9.0, 0, 0), head=(9.0, 0, 0))
    pull = bc.with_right_arm(up, arm_r=mouth_arm((0.0, -0.1, -0.07)), neck=(2.0, 0, 0), head=(-2.0, 0, 0))
    chew = bc.with_right_arm(h, neck=(3.0, 0, 0), head=(5.0, 0, 0))
    chew2 = bc.with_right_arm(h, neck=(1.0, 0, 0), head=(-1.0, 0, 0))
    return bc.oneshot([(0.0, h), (0.22, up), (0.3, bite), (0.4, pull), (0.52, bite), (0.62, pull),
                       (0.86, chew), (1.0, chew2), (1.12, chew), (1.4, h)], t)


# When the fruit changes during eat_food (s): the first bite shows, the second, and it is gone.
EAT_MARKS = {"bite1": 0.3, "bite2": 0.52, "gone": 0.86}


FOOD_GRIP_FP = np.array([-0.15, -0.4, 1.42])      # first person: the hand up in the lower right of the view


def clip_hold_food_fp(t, T=2.0):
    s = clip_hold_food(t, T)
    ph = 2 * math.pi * t / T
    s["arm_r"] = food_arm(FOOD_GRIP_FP + np.array([0, 0, 0.004 * math.sin(ph)]))
    return s


def clip_eat_food_fp(t):
    """First person: the same eat, starting from the first-person hold."""
    h = clip_hold_food_fp(0.0)
    s = clip_eat_food(t)
    if t < 0.22:                      # blend out of the first-person hold
        k = t / 0.22
        return bc.oneshot([(0.0, h), (1.0, clip_eat_food(0.22))], k)
    if t > 1.12:
        k = (t - 1.12) / 0.28
        return bc.oneshot([(0.0, clip_eat_food(1.12)), (1.0, h)], k)
    return s
