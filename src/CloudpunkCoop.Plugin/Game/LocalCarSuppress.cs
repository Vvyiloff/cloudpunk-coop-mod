using UnityEngine;

namespace CloudpunkCoop.Game
{
    /// <summary>
    /// Keeps the original singletons as invisible streaming anchors, not simulated cars.
    /// Saves every state changed here so a dropped connection returns control locally.
    /// </summary>
    public static class LocalCarSuppress
    {
        private static GameObject _car;
        private static GameObject _player;
        private static Rigidbody _body;
        private static bool _carActive, _playerActive, _kinematic, _collisions;
        private static Vector3 _carPosition, _playerPosition, _velocity, _angularVelocity;
        private static Quaternion _carRotation, _playerRotation;
        public static bool Active => _car != null;

        public static void Begin()
        {
            if (Active) return;
            _car = GameRefs.PlayerCar.gameObject;
            _carActive = _car.activeSelf;
            _carPosition = _car.transform.position;
            _carRotation = _car.transform.rotation;
            _body = GameRefs.PlayerCar.rBody;
            if (_body != null)
            {
                _kinematic = _body.isKinematic;
                _collisions = _body.detectCollisions;
                _velocity = _body.velocity;
                _angularVelocity = _body.angularVelocity;
                _body.velocity = Vector3.zero;
                _body.angularVelocity = Vector3.zero;
                _body.isKinematic = true;
                _body.detectCollisions = false;
            }
            // Disabling the whole object also blocks native FixedUpdate and all its colliders,
            // lights, audio and particles, without relying on an inlined input getter.
            _car.SetActive(false);
            if (GameRefs.Player != null)
            {
                _player = GameRefs.Player.gameObject;
                _playerActive = _player.activeSelf;
                _playerPosition = _player.transform.position;
                _playerRotation = _player.transform.rotation;
                _player.SetActive(false);
            }
        }

        public static void Follow(Vector3 position, Quaternion rotation)
        {
            if (_car == null) return;
            _car.SetActive(false);
            _car.transform.SetPositionAndRotation(position, rotation);
            if (_player != null && !GuestMobility.LocalOnFoot)
            {
                _player.SetActive(false);
                _player.transform.SetPositionAndRotation(position, rotation);
            }
        }

        public static void Restore()
        {
            if (_car != null)
            {
                _car.transform.SetPositionAndRotation(_carPosition, _carRotation);
                if (_body != null)
                {
                    _body.isKinematic = _kinematic;
                    _body.detectCollisions = _collisions;
                    if (!_kinematic)
                    {
                        _body.velocity = _velocity;
                        _body.angularVelocity = _angularVelocity;
                    }
                }
                _car.SetActive(_carActive);
            }
            if (_player != null)
            {
                _player.transform.SetPositionAndRotation(_playerPosition, _playerRotation);
                _player.SetActive(_playerActive);
            }
            _car = null;
            _player = null;
            _body = null;
        }
    }
}
